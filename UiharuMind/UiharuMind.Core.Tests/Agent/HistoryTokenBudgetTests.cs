using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Agent;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死历史 token 预算裁剪的语义：保最新裁最旧、窗口起点不落在工具调用组内部
/// (孤儿工具结果会被模型 API 拒绝)、裁剪提示带 _attribution(不持久化、不渲染为用户气泡)。
/// 估算器由测试注入,不依赖真实分词。
/// </summary>
public class HistoryTokenBudgetTests
{
    private static int OneTokenPerMessage(ChatMessage _) => 1;

    private static List<ChatMessage> PlainHistory(int count)
    {
        return Enumerable.Range(1, count).Select(i => new ChatMessage(ChatRole.User, $"m{i}")).ToList();
    }

    [Fact]
    public void ZeroBudget_MeansUnlimited()
    {
        List<ChatMessage> history = PlainHistory(5);
        List<ChatMessage> window = SessionChatHistoryProvider.TrimToTokenBudget(history, 0, OneTokenPerMessage);

        Assert.Equal(5, window.Count);
        Assert.NotSame(history, window); //始终返回新列表,供框架安全枚举
    }

    [Fact]
    public void UnderBudget_ReturnsAllWithoutNotice()
    {
        List<ChatMessage> window =
            SessionChatHistoryProvider.TrimToTokenBudget(PlainHistory(5), 10, OneTokenPerMessage);

        Assert.Equal(5, window.Count);
        Assert.DoesNotContain(window, x => x.Text.Contains("trimmed"));
    }

    [Fact]
    public void OverBudget_KeepsNewestAndPrependsAttributedNotice()
    {
        List<ChatMessage> window =
            SessionChatHistoryProvider.TrimToTokenBudget(PlainHistory(10), 3, OneTokenPerMessage);

        Assert.Equal(4, window.Count); //3 条最新 + 1 条裁剪提示
        Assert.Contains("trimmed", window[0].Text);
        Assert.Equal("m8", window[1].Text);
        Assert.Equal("m10", window[^1].Text);

        // 提示消息必须带溯源标记:据此不入历史、不渲染为用户气泡
        Assert.False(SessionChatHistoryProvider.IsOwnedByUs(window[0]));
    }

    [Fact]
    public void WindowStart_NeverLandsOnOrphanToolResult()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "question"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read", null)]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "file content")]),
            new(ChatRole.Assistant, "answer"),
            new(ChatRole.User, "follow-up"),
        ];

        // 预算 3:天然起点是工具结果消息(index 2),必须向后跳过到干净边界
        List<ChatMessage> window = SessionChatHistoryProvider.TrimToTokenBudget(history, 3, OneTokenPerMessage);

        ChatMessage firstReal = window.First(x => !x.Text.Contains("trimmed"));
        Assert.Equal("answer", firstReal.Text);
        Assert.DoesNotContain(window, x => x.Contents.Any(c => c is FunctionResultContent));
    }

    [Fact]
    public void SingleOversizedNewestMessage_IsStillKept()
    {
        List<ChatMessage> history = PlainHistory(3);
        List<ChatMessage> window =
            SessionChatHistoryProvider.TrimToTokenBudget(history, 1, _ => 100);

        Assert.Contains(window, x => x.Text == "m3"); //当前轮次不能没有上文
        Assert.DoesNotContain(window, x => x.Text == "m2");
    }
}
