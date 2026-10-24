using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 记忆压缩末尾的回查段。和委派清单同一条取舍：<b>不由模型写</b>，从历史现算——
/// 用户原话是约束的第一手来源，"别动 X"这种话正是摘要最容易丢的。
/// 它只数<b>用户本人</b>说的话，并且有界：条数封顶 15，逐条原文不截断。
/// </summary>
public class HistoryHandoffRecallTests
{
    [Fact]
    public void QuotesOnlyWhatTheUserTyped()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "别动 vendor 目录"),
            Annotated("子代理交回的报告", ChatMessageAnnotations.SubAgentReport),
            Annotated("派活方插的话", ChatMessageAnnotations.ParentInterjection),
            Annotated("[小明]: 群里别人的发言", ChatMessageAnnotations.GroupDelivery),
            Annotated("后台任务的构建日志末尾", ChatMessageAnnotations.BackgroundTaskReport),
            Annotated("目标模式的系统注入", ChatMessageAnnotations.GoalModeInjection),
            new(ChatRole.Assistant, "好的"),
            new(ChatRole.User, "继续"),
        ];

        string recall = HistoryHandoff.BuildRecall(history, null);

        Assert.Contains("- #1: 别动 vendor 目录", recall);
        Assert.Contains("- #8: 继续", recall);
        Assert.DoesNotContain("报告", recall);
        Assert.DoesNotContain("构建日志", recall);
        Assert.DoesNotContain("插的话", recall);
        Assert.DoesNotContain("群里", recall);
        Assert.DoesNotContain("系统注入", recall);
    }

    /// <summary>条数封顶：只带最近 15 条，不按篇幅截</summary>
    [Fact]
    public void ShortMessages_AreCappedAtFifteen()
    {
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条"))
            .ToList();

        List<string> quotes = QuoteLines(HistoryHandoff.BuildRecall(history, null));

        Assert.Equal(15, quotes.Count);
        Assert.StartsWith("- #16:", quotes[0]); //连续地往前带,不跳条
        Assert.StartsWith("- #30:", quotes[^1]); //最新的在最后
    }

    [Fact]
    public void LongMessages_AreKeptVerbatim_AndCappedAtFifteen()
    {
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        List<string> quotes = QuoteLines(HistoryHandoff.BuildRecall(history, null));

        Assert.Equal(15, quotes.Count);
        Assert.All(quotes, line => Assert.Contains(new string('字', 1000), line)); //原文不截断
        Assert.StartsWith("- #16:", quotes[0]);
        Assert.StartsWith("- #30:", quotes[^1]);
    }

    /// <summary>只数条数、不比长度：即使只有两条也全带，超长粘贴原样进回忆段</summary>
    [Fact]
    public void MessageCount_IsTheOnlyBound_NotLength()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, new string('旧', 300)),
            new(ChatRole.User, new string('新', 300)),
        ];

        string recall = HistoryHandoff.BuildRecall(history, null);

        Assert.Contains("- #1:", recall);
        Assert.Contains("- #2:", recall);
    }

    [Fact]
    public void PointsToTheTranscript_OnlyWhenOneWasWritten()
    {
        List<ChatMessage> history = Enumerable.Range(1, 30)
            .Select(i => new ChatMessage(ChatRole.User, $"第{i}条" + new string('字', 1000)))
            .ToList();

        string withTranscript = HistoryHandoff.BuildRecall(history, "/data/s.transcript.md");
        string without = HistoryHandoff.BuildRecall(history, null);

        int omitted = 30 - QuoteLines(withTranscript).Count;
        Assert.Contains("\"/data/s.transcript.md\"", withTranscript);
        Assert.Contains($"更早的 {omitted} 条用户消息", withTranscript); //带不下的那些指向转录
        Assert.Contains("`Grep`", withTranscript);
        Assert.DoesNotContain("Grep", without); //搜不了的会话不给一个搜不了的提示
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
        Assert.Equal(string.Empty, HistoryHandoff.BuildRecall([new ChatMessage(ChatRole.Assistant, "hi")], null));
    }

    private static List<string> QuoteLines(string recall) =>
        recall.Split('\n').Where(line => line.StartsWith("- #", StringComparison.Ordinal)).ToList();

    private static ChatMessage Annotated(string text, string annotation) =>
        new(ChatRole.User, text)
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [annotation] = "x" },
        };
}
