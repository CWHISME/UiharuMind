using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 交接文档末尾的回查段。和委派清单同一条取舍：<b>不由模型写</b>，从历史现算——
/// 用户原话是约束的第一手来源，"别动 X"这种话正是摘要最容易丢的。
/// 同时它必须有界：交接文档有篇幅上限，用户消息却没有。
/// </summary>
public class HistoryHandoffRecallTests
{
    private const int ContextLength = 128_000;

    [Fact]
    public void QuotesOnlyWhatTheUserTyped()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "别动 vendor 目录"),
            Annotated("子代理交回的报告", ChatMessageAnnotations.SubAgentReport),
            Annotated("派活方插的话", ChatMessageAnnotations.ParentInterjection),
            Annotated("[小明]: 群里别人的发言", ChatMessageAnnotations.GroupDelivery),
            new(ChatRole.Assistant, "好的"),
            new(ChatRole.User, "继续"),
        ];

        string recall = HistoryHandoff.BuildRecall(history, null, ContextLength);

        Assert.Contains("- #1: 别动 vendor 目录", recall);
        Assert.Contains("- #6: 继续", recall);
        Assert.DoesNotContain("报告", recall);
        Assert.DoesNotContain("插的话", recall);
        Assert.DoesNotContain("群里", recall);
    }

    [Fact]
    public void KeepsTheNewestTen_OldestFirst_EachCut()
    {
        List<ChatMessage> history = Enumerable.Range(1, 15)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        string recall = HistoryHandoff.BuildRecall(history, null, ContextLength);

        Assert.DoesNotContain("#5:", recall);
        Assert.True(recall.IndexOf("#6:", StringComparison.Ordinal) < recall.IndexOf("#15:", StringComparison.Ordinal));
        Assert.All(recall.Split('\n').Where(x => x.StartsWith("- #")), line => Assert.True(line.Length < 320, line));
    }

    /// <summary>预算紧时照样带上最近那一条：它往往就是当前这件事本身</summary>
    [Fact]
    public void TinyBudget_StillKeepsTheLatestMessage()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, new string('旧', 300)),
            new(ChatRole.User, new string('新', 300)),
        ];

        string recall = HistoryHandoff.BuildRecall(history, null, contextLength: 1000);

        Assert.Contains("#2:", recall);
        Assert.DoesNotContain("#1:", recall);
    }

    [Fact]
    public void PointsToTheTranscript_OnlyWhenOneWasWritten()
    {
        List<ChatMessage> history = Enumerable.Range(1, 12)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条"))
            .ToList();

        string withTranscript = HistoryHandoff.BuildRecall(history, "/data/s.transcript.md", ContextLength);
        string without = HistoryHandoff.BuildRecall(history, null, ContextLength);

        Assert.Contains("\"/data/s.transcript.md\"", withTranscript);
        Assert.Contains("2 earlier user message(s)", withTranscript);
        Assert.Contains("`Grep`", withTranscript);
        Assert.DoesNotContain("Grep", without); //搜不了的会话不给一个搜不了的提示
    }

    [Fact]
    public void NothingToRecall_ProducesNothing()
    {
        Assert.Equal(string.Empty, HistoryHandoff.BuildRecall([new ChatMessage(ChatRole.Assistant, "hi")], null, ContextLength));
    }

    private static ChatMessage Annotated(string text, string annotation) =>
        new(ChatRole.User, text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [annotation] = "x" },
        };
}
