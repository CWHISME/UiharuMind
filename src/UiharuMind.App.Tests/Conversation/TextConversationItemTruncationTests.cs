using UiharuMind.Features.Conversation.Items;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 文本气泡的截断规则：只对<b>旁白类</b>（开场白 / 子代理后续报告）启用。
///
/// 旁白是静态的「扫一眼」内容，语义与工具结果相同——超长时只把头部交给气泡排版，
/// 全文去独立全文窗（<c>FullTextWindow</c>）。用户/助手消息是流式的、正在被阅读，
/// 截断会破坏阅读体验，因此即使超长也原样透传。
/// </summary>
public class TextConversationItemTruncationTests
{
    private static string Lines(int count, string text = "line")
        => string.Join('\n', Enumerable.Range(0, count).Select(i => $"{text}{i}"));

    [Fact]
    public void LongNarration_IsTruncated_AndKeepsTheOriginal()
    {
        string text = Lines(5000);
        TextConversationItem item = new(isUser: false, isNarration: true);

        item.Message = text;

        Assert.True(item.IsTruncated);
        Assert.Equal(text, item.Message); //原文一字不少,查看全文拿的是它
        Assert.True(item.DisplayMessage.Length < text.Length);
        Assert.StartsWith("line0\n", item.DisplayMessage); //头部锚定
    }

    [Fact]
    public void ShortNarration_PassesThroughUntouched()
    {
        const string text = "ok\ndone";
        TextConversationItem item = new(isUser: false, isNarration: true);

        item.Message = text;

        Assert.False(item.IsTruncated);
        Assert.Equal(text, item.DisplayMessage);
    }

    /// <summary>用户/助手消息即使超长也不截断——它是流式的、正在被阅读</summary>
    [Fact]
    public void LongAssistantMessage_IsNeverTruncated()
    {
        string text = Lines(5000);
        TextConversationItem item = new(isUser: false, isNarration: false);

        item.Message = text;

        Assert.False(item.IsTruncated);
        Assert.Equal(text, item.DisplayMessage);
    }

    /// <summary>换一份正文要整份重算：上一份的截断视图不该漏给下一份</summary>
    [Fact]
    public void ANewMessage_RebuildsTheView()
    {
        TextConversationItem item = new(isUser: false, isNarration: true);
        item.Message = Lines(5000);

        item.Message = "ok";

        Assert.False(item.IsTruncated);
        Assert.Equal("ok", item.DisplayMessage);
    }

    /// <summary>
    /// 气泡正文绑的是 <c>DisplayMessage</c> 而不是 <c>Message</c>,因此 Message 每次变化
    /// 都必须转告 DisplayMessage——否则流式期间助手消息收不到更新、一直停在空白,
    /// 只有切走切回整段重建才恢复(回归测试,曾踩过)。
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MessageChange_NotifiesDisplayMessage(bool isNarration)
    {
        TextConversationItem item = new(isUser: false, isNarration: isNarration);
        bool notified = false;
        item.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(TextConversationItem.DisplayMessage)) notified = true;
        };

        item.Message = "hello";

        Assert.True(notified, "Message 变化应同步通知 DisplayMessage");
        Assert.Equal("hello", item.DisplayMessage);
    }
}
