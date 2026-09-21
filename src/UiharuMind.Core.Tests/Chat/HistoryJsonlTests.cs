using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉死历史 JSONL 的两条关键性质:
/// ① 一行一条消息可完整往返(含多态 AIContent);
/// ② 残缺尾行(进程中断的典型产物)被跳过,其余历史不受影响。
/// </summary>
public class HistoryJsonlTests
{
    [Fact]
    public void MessagesRoundTripThroughLines()
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.User, "你好\n第二行"),
            new(ChatRole.Assistant, "回复"),
        ];

        string text = HistoryJsonl.SerializeLines(messages);
        List<ChatMessage> restored = HistoryJsonl.Parse(text.Split('\n'));

        Assert.Equal(2, restored.Count);
        Assert.Equal("你好\n第二行", restored[0].Text);
        Assert.Equal(ChatRole.Assistant, restored[1].Role);
        Assert.Equal("回复", restored[1].Text);
    }

    [Fact]
    public void MalformedTrailingLineIsSkipped()
    {
        string text = HistoryJsonl.SerializeLines([new ChatMessage(ChatRole.User, "完整的一条")]);
        string truncated = text + "{\"role\":\"assistant\",\"contents\":[{\"$type\":\"te";

        List<ChatMessage> restored = HistoryJsonl.Parse(truncated.Split('\n'));

        Assert.Single(restored);
        Assert.Equal("完整的一条", restored[0].Text);
    }
}
