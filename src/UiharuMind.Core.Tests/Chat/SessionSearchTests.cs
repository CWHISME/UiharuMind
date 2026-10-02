using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话内搜索的口径：搜消息历史、与界面显示一致（注入的不搜、点名调用搜敲的那一行），
/// 默认只搜正文——思考与工具结果要用户自己勾上
/// </summary>
public class SessionSearchTests
{
    [Fact]
    public void Find_MatchesTextIgnoringCase_InHistoryOrder()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "帮我看看 Avalonia 的布局"),
            new(ChatRole.Assistant, "好的"),
            new(ChatRole.Assistant, "avalonia 用的是两遍布局"),
        ];

        List<SessionSearchHit> hits = SessionSearch.Find(history, "AVALONIA");

        Assert.Equal([0, 2], hits.Select(x => x.MessageIndex));
        Assert.All(hits, x => Assert.Equal(ESearchHitKind.Text, x.Kind));
        Assert.Same(history[2], hits[1].Message);
    }

    /// <summary>审批回应界面上不画，搜索也不该搜到（两边曾各写一份口径而分叉）</summary>
    [Fact]
    public void Find_SkipsApprovalResponses()
    {
        Assert.Empty(SessionSearch.Find([ChatMessageDisplayTests.ApprovalResponse("同意写入")], "同意"));
    }

    [Fact]
    public void Find_BlankQuery_ReturnsNothing()
    {
        Assert.Empty(SessionSearch.Find([new ChatMessage(ChatRole.User, "随便")], "  "));
    }

    [Fact]
    public void Find_ThinkingIsOptIn()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new TextReasoningContent("先排查缓存"), new TextContent("结论在这")]),
            new(ChatRole.Assistant, "<think>缓存命中率</think>正文"),
        ];

        Assert.Empty(SessionSearch.Find(history, "缓存"));

        List<SessionSearchHit> hits = SessionSearch.Find(history, "缓存", new SessionSearchOptions(IncludeThinking: true));
        Assert.Equal([0, 1], hits.Select(x => x.MessageIndex));
        Assert.All(hits, x => Assert.Equal(ESearchHitKind.Thinking, x.Kind));
    }

    [Fact]
    public void Find_BodyWinsOverThinkingInTheSameMessage()
    {
        List<ChatMessage> history =
            [new(ChatRole.Assistant, [new TextReasoningContent("缓存"), new TextContent("缓存已修")])];

        SessionSearchHit hit = Assert.Single(SessionSearch.Find(history, "缓存", new SessionSearchOptions(true, true)));

        Assert.Equal(ESearchHitKind.Text, hit.Kind);
    }

    [Fact]
    public void Find_ToolsAreOptIn()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "Grep", new Dictionary<string, object?> { ["pattern"] = "LastCached" })]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "TurnUsageLedger.cs: LastCachedInput")]),
        ];

        Assert.Empty(SessionSearch.Find(history, "LastCached"));

        List<SessionSearchHit> hits = SessionSearch.Find(history, "LastCached", new SessionSearchOptions(IncludeTools: true));
        Assert.Equal([0, 1], hits.Select(x => x.MessageIndex));
        Assert.All(hits, x => Assert.Equal(ESearchHitKind.Tool, x.Kind));
    }

    [Fact]
    public void Find_KnowledgeSnippetsCountAsTools()
    {
        ChatMessage knowledge = new(ChatRole.Tool, "检索到的片段：缓存");
        knowledge.AdditionalProperties = new() { [ChatMessageAnnotations.Knowledge] = true };

        Assert.Empty(SessionSearch.Find([knowledge], "缓存"));
        Assert.Single(SessionSearch.Find([knowledge], "缓存", new SessionSearchOptions(IncludeTools: true)));
    }

    [Fact]
    public void Find_SkipsInjectedButKeepsHistoryEcho()
    {
        ChatMessage injected = new ChatMessage(ChatRole.User, "todo 快照：缓存")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.AIContextProvider, "todo");
        ChatMessage echoed = new ChatMessage(ChatRole.User, "我问过缓存")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.ChatHistory, "history");

        SessionSearchHit hit = Assert.Single(SessionSearch.Find([injected, echoed], "缓存"));

        Assert.Same(echoed, hit.Message);
    }

    [Fact]
    public void Find_NamedSkillSearchesWhatTheUserTyped()
    {
        // 落盘的是技能正文,气泡显示的是用户敲的那一行——搜的得是看得见的那个
        ChatMessage message = new(ChatRole.User, "技能正文：一大段说明");
        message.AdditionalProperties = new()
        {
            [ChatMessageAnnotations.NamedSkill] = "review",
            [ChatMessageAnnotations.NamedSkillInput] = "/review 缓存那块",
        };

        Assert.Single(SessionSearch.Find([message], "缓存"));
        Assert.Empty(SessionSearch.Find([message], "一大段说明"));
    }

    [Fact]
    public void Find_HandoffNoteSearchesItsBody()
    {
        ChatMessage note = HistoryHandoff.CreateNote("之前在修缓存统计");

        Assert.Single(SessionSearch.Find([note], "缓存统计"));
    }

    [Fact]
    public void SnippetOf_CollapsesWhitespaceAndMarksCuts()
    {
        string text = new string('甲', 40) + "\n\n命中  词\n" + new string('乙', 100);

        string snippet = SessionSearch.SnippetOf(text, 42, 2);

        Assert.StartsWith("…", snippet);
        Assert.EndsWith("…", snippet);
        Assert.Contains("命中 词", snippet);
        Assert.DoesNotContain("\n", snippet);
    }
}
