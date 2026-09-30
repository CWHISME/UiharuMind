using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 交接文档末尾的回查段。和委派清单同一条取舍：<b>不由模型写</b>，从历史现算——
/// 用户原话是约束的第一手来源，"别动 X"这种话正是摘要最容易丢的。
/// 同时它必须有界：交接文档有篇幅上限，用户消息却没有——界由篇幅预算定，不设条数上限。
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

    /// <summary>不设条数上限：短消息再多也都带上，成本由篇幅预算兜住</summary>
    [Fact]
    public void ShortMessages_AreNotCappedByCount()
    {
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条"))
            .ToList();

        string recall = HistoryHandoff.BuildRecall(history, null, ContextLength);

        Assert.Equal(30, QuoteLines(recall).Count);
    }

    [Fact]
    public void LongMessages_AreCut_AndBoundedByBudget_KeepingTheNewestOldestFirst()
    {
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        List<string> quotes = QuoteLines(HistoryHandoff.BuildRecall(history, null, ContextLength));

        Assert.InRange(quotes.Count, 2, 29); //被预算截住,不是全量
        Assert.All(quotes, line => Assert.True(line.Length < 320, line)); //逐条截断
        Assert.StartsWith("- #30:", quotes[^1]); //最新的在最后
        Assert.StartsWith($"- #{31 - quotes.Count}:", quotes[0]); //连续地往前带,不跳条
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
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        string withTranscript = HistoryHandoff.BuildRecall(history, "/data/s.transcript.md", ContextLength);
        string without = HistoryHandoff.BuildRecall(history, null, ContextLength);

        int omitted = 30 - QuoteLines(withTranscript).Count;
        Assert.Contains("\"/data/s.transcript.md\"", withTranscript);
        Assert.Contains($"{omitted} earlier user message(s)", withTranscript); //带不下的那些指向转录
        Assert.Contains("`Grep`", withTranscript);
        Assert.DoesNotContain("Grep", without); //搜不了的会话不给一个搜不了的提示
    }

    /// <summary>原话预算按输入预算单独算：正文篇幅上限封顶 8000 token，附加段不再跟着它封顶</summary>
    [Fact]
    public void Budget_ScalesWithTheContext_NotCappedByTheNoteLimit()
    {
        List<ChatMessage> history = Enumerable.Range(1, 300)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        int at128K = QuoteLines(HistoryHandoff.BuildRecall(history, null, ContextLength)).Count;
        int at1M = QuoteLines(HistoryHandoff.BuildRecall(history, null, 1_000_000)).Count;

        Assert.True(at1M > at128K, $"128k: {at128K}, 1M: {at1M}");
    }

    [Fact]
    public void WithAppendix_SeparatesGeneratedSectionsFromTheModelsNote()
    {
        Assert.Equal("正文", HistoryHandoff.WithAppendix("正文", string.Empty, string.Empty));
        Assert.Equal($"正文\n\n{HistoryHandoff.AppendixHeading}\nA\n\nB",
            HistoryHandoff.WithAppendix("正文", "\nA", string.Empty, "\nB\n"));
    }

    [Fact]
    public void NothingToRecall_ProducesNothing()
    {
        Assert.Equal(string.Empty, HistoryHandoff.BuildRecall([new ChatMessage(ChatRole.Assistant, "hi")], null, ContextLength));
    }

    private static List<string> QuoteLines(string recall) =>
        recall.Split('\n').Where(line => line.StartsWith("- #", StringComparison.Ordinal)).ToList();

    private static ChatMessage Annotated(string text, string annotation) =>
        new(ChatRole.User, text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [annotation] = "x" },
        };
}
