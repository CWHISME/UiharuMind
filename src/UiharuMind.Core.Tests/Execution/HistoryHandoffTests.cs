using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死交接文档式压缩的三件事：什么时候触发、喂给模型的历史从哪开始、标记怎么认。
///
/// 起点判定是承重的：交接文档<b>本身必须包含在供给区间内</b>——它是压缩之后模型能看到的
/// 全部前情，漏掉它等于那段历史白压；而它之前的消息一条都不能进，否则压缩没省下任何东西。
/// </summary>
public class HistoryHandoffTests
{
    private static ChatMessage User(string text) => new(ChatRole.User, text);

    [Fact]
    public void ShouldWrite_FiresAboveThresholdOfInputBudget()
    {
        int budget = HistoryCompaction.InputBudgetFor(128_000); //119808

        Assert.False(HistoryHandoff.ShouldWrite((long)(budget * 0.79), 128_000));
        Assert.True(HistoryHandoff.ShouldWrite((long)(budget * 0.81), 128_000));
    }

    /// <summary>
    /// 服务端少报时，水位必须靠我们自己的估算兜住。
    ///
    /// 复现 GLM4-Flash：它的 <c>prompt_tokens</c> 不含工具定义，报的约是真实的一半。
    /// 只信报告占用的话这条水位<b>永远不触发</b>——三条水位里唯一会调模型、
    /// 也唯一能保住上下文的那条就此失效。见 ADR 0009。
    /// </summary>
    [Fact]
    public void ShouldWrite_UsesEffectiveUsageWhenServerUnderReports()
    {
        int budget = HistoryCompaction.InputBudgetFor(128_000);
        long ours = (long)(budget * 0.85); //我们估的:已经过了水位
        long reported = ours / 2; //服务端只报一半

        TurnUsageLedger ledger = new() { ContextLength = 128_000, EstimatedInput = ours };
        ledger.Add(new UsageDetails { InputTokenCount = reported });

        Assert.False(HistoryHandoff.ShouldWrite(ledger.LastInput, 128_000), "只信服务端的话这一条压根不会触发");
        Assert.True(HistoryHandoff.ShouldWrite(ledger.EffectiveInput, 128_000));
    }

    /// <summary>
    /// 折叠把发出去的那份压回交接线之下时，交接照样要来：它替换的是原始历史，被压住的话只会越拖越大，
    /// 最后发出去的那一发原始历史可能比窗口还大
    /// </summary>
    [Fact]
    public void ShouldWrite_StillFiresAfterFoldingPushedTheSentHistoryBelowTheLine()
    {
        int budget = HistoryCompaction.InputBudgetFor(128_000);
        TurnUsageLedger ledger = new()
        {
            ContextLength = 128_000,
            EstimatedInput = (long)(budget * 0.7), //折叠之后发出去的
            CompactedInput = (long)(budget * 0.2), //折掉的那截
        };

        Assert.False(HistoryHandoff.ShouldWrite(ledger.EffectiveInput, 128_000), "只看发出去的那份就被折叠压住了");
        Assert.True(HistoryHandoff.ShouldWrite(ledger.RawInput, 128_000));
    }

    /// <summary>服务端报得比我们估的高时以它为准——取大是两侧都兜，不是单向替换</summary>
    [Fact]
    public void EffectiveUsage_KeepsTheLargerSide()
    {
        TurnUsageLedger ledger = new() { EstimatedInput = 1000 };
        ledger.Add(new UsageDetails { InputTokenCount = 3000 });

        Assert.Equal(3000, ledger.EffectiveInput);
        Assert.Equal(0, ledger.UnreportedInput); //服务端报得更高时没有"未计入"那一段
    }

    [Fact]
    public void ShouldWrite_FiresBeforeFrameworkTruncation()
    {
        //必须早于框架截断的水位,否则交接还没写成历史就被截掉了
        Assert.True(HistoryHandoff.Threshold < HistoryCompaction.TruncationThreshold);
    }

    /// <summary>
    /// 三条水位换到<b>全量占用</b>那根轴上（进度条与配色用的就是它）之后，次序必须仍是
    /// 交接 → 折叠 → 截断。
    ///
    /// 这一条会静默坏掉：折叠与截断乘在<b>历史额度</b>上（预算减固定开销），交接乘在<b>输入预算</b>上，
    /// 两者分母不同。折叠一旦落到交接前面，历史每次都先撞上它，折一次、轮末交接再一次，缓存断两回；
    /// <see cref="ShouldWrite_FiresBeforeFrameworkTruncation"/> 只比常数，看不见这个。
    /// </summary>
    [Theory]
    [InlineData(128_000, 0)]
    [InlineData(128_000, 25_600)] //GLM + Unity 那套
    [InlineData(128_000, 60_000)] //固定开销吃掉半个预算的极端情形
    [InlineData(8192, 2000)] //小上下文的本地模型
    [InlineData(1_048_576, 10_000)]
    public void Watermarks_StayOrderedOnTheFullUsageAxis(int context, int fixedOverhead)
    {
        int budget = HistoryCompaction.InputBudgetFor(context);
        int quota = HistoryCompaction.HistoryQuotaFor(context, fixedOverhead);

        double eviction = fixedOverhead + quota * HistoryCompaction.ToolEvictionThreshold;
        double handoff = budget * HistoryHandoff.Threshold;
        double truncation = fixedOverhead + quota * HistoryCompaction.TruncationThreshold;

        Assert.True(handoff < eviction, $"交接 {handoff:0} 必须早于折叠 {eviction:0}");
        Assert.True(eviction < truncation, $"折叠 {eviction:0} 必须早于截断 {truncation:0}");
        //截断之后请求必须还发得出去,这正是分母改扣固定开销要保证的事
        Assert.True(truncation < context, $"截断水位 {truncation:0} 必须仍在上限 {context} 之内");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ShouldWrite_NeverFiresWithoutAContextLength(int contextLength)
    {
        Assert.False(HistoryHandoff.ShouldWrite(long.MaxValue, contextLength));
    }

    [Fact]
    public void CreateNote_IsRecognisableAndCarriesTheBody()
    {
        ChatMessage note = HistoryHandoff.CreateNote("做完了 A，正在做 B。");

        Assert.True(HistoryHandoff.IsNote(note));
        //是 assistant:「我」以第一人称整理的记忆,是模型自己的第一条消息,新一轮从这里接着往下说
        Assert.Equal(ChatRole.Assistant, note.Role);
        Assert.Contains("做完了 A", note.Text);
        //不能带 _attribution:那个键的含义是「不落盘」,记忆压缩丢了就等于那段历史白压
        Assert.False(note.AdditionalProperties!.ContainsKey(ChatMessageAnnotations.Attribution));
    }

    [Fact]
    public void NoteBody_StripsTheModelFacingTitle()
    {
        ChatMessage note = HistoryHandoff.CreateNote("正文");

        Assert.Equal("正文", HistoryHandoff.NoteBody(note.Text));
        Assert.Equal("没有标题的文本", HistoryHandoff.NoteBody("没有标题的文本"));
    }

    [Fact]
    public void NoteBody_StripsTheLegacyEnglishTitle()
    {
        //改名之前写下的记忆压缩,标题还是 "Context handoff"
        Assert.Equal("正文", HistoryHandoff.NoteBody("[Context handoff]\n正文"));
    }

    [Fact]
    public void ComposeBody_StartsWithOpening_AndEndsWithLastSentence()
    {
        string body = HistoryHandoff.ComposeBody("正文", "好的，马上办。");

        Assert.StartsWith("我来整理这段历史记忆。", body);
        Assert.Contains("上一轮我的最后一句：", body);
        Assert.EndsWith("好的，马上办。", body);
    }

    [Fact]
    public void ComposeBody_WithoutLastSentence_OmitsTheSection()
    {
        Assert.Equal("我来整理这段历史记忆。\n\n正文", HistoryHandoff.ComposeBody("正文", null));
    }

    /// <summary>
    /// 清空记忆提示复用交接的供应链:带同一注解、供给起点认它,界面上只在值上区分标题
    /// </summary>
    [Fact]
    public void CreateClearNote_ReusesHandoffSupplyChain_AndIsDistinguishable()
    {
        ChatMessage message = HistoryHandoff.CreateClearNote("我把之前的记忆清理掉了。");

        Assert.True(HistoryHandoff.IsNote(message), "供给起点要认它:它之后的窗口从这条开始");
        Assert.True(HistoryHandoff.IsClear(message), "界面靠值区分标题:记忆已清理/记忆压缩");
        Assert.Contains("之前的记忆", HistoryHandoff.NoteBody(message.Text));
    }

    [Fact]
    public void NoteBody_StripsTheClearTitle()
    {
        Assert.Equal("正文", HistoryHandoff.NoteBody(HistoryHandoff.CreateClearNote("正文").Text));
    }

    [Fact]
    public void ComposeClearBody_WithTranscriptPath_ListsItForRecalling()
    {
        string body = HistoryHandoff.ComposeClearBody("/tmp/uiharu/s1.transcript.md");

        Assert.Contains("/tmp/uiharu/s1.transcript.md", body);
    }

    [Fact]
    public void ComposeClearBody_WithoutTranscriptPath_OmitsThePath()
    {
        string body = HistoryHandoff.ComposeClearBody(null);

        Assert.DoesNotContain("导出到", body);
    }

    /// <summary>清空提示与交接文档同一条起点判定:模型从它开始,之前的内容不可见</summary>
    [Fact]
    public void SupplyStart_PointsAtTheClearMessage()
    {
        List<ChatMessage> history =
        [
            User("第一条"),
            HistoryHandoff.CreateClearNote("我把之前的记忆清理掉了。"),
        ];

        Assert.Equal(1, HistoryHandoff.SupplyStartIndex(history));
    }

    [Fact]
    public void LastAssistantSentence_TakesTheLastSentenceOfTheLastAssistantMessage()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "a"),
            new(ChatRole.Assistant, "先看 a.cs。再看 b.cs。"),
            new(ChatRole.User, "b"),
        ];

        Assert.Equal("再看 b.cs。", HistoryHandoff.LastAssistantSentence(history));
    }

    [Fact]
    public void LastAssistantSentence_SkipsNotesNarrationAndTextlessMessages()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, "真正的上一句。"),
            HistoryHandoff.CreateNote("上一份记忆压缩"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]), //纯工具调用没有正文
        ];

        Assert.Equal("真正的上一句。", HistoryHandoff.LastAssistantSentence(history));
    }

    [Fact]
    public void LastAssistantSentence_WithoutAnyAssistantText_ReturnsNull()
    {
        ChatMessage narration = new(ChatRole.Assistant, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = "x" },
        };

        Assert.Null(HistoryHandoff.LastAssistantSentence([narration, HistoryHandoff.CreateNote("上一份记忆压缩")]));
        Assert.Null(HistoryHandoff.LastAssistantSentence([new(ChatRole.User, "只有用户")]));
        Assert.Null(HistoryHandoff.LastAssistantSentence([]));
    }

    [Fact]
    public void LastSentence_HandlesEnglishAndVersionNumbers()
    {
        Assert.Equal("next.", HistoryHandoff.LastAssistantSentence([new ChatMessage(ChatRole.Assistant, "first. next.")]));
        Assert.Equal("版本 v1.2.3", HistoryHandoff.LastAssistantSentence([new ChatMessage(ChatRole.Assistant, "版本 v1.2.3")]));
        Assert.Equal("好的", HistoryHandoff.LastAssistantSentence([new ChatMessage(ChatRole.Assistant, "你好。好的")]));
    }

    [Fact]
    public void SupplyStart_IsZeroWithoutAnyNote()
    {
        List<ChatMessage> history = [User("a"), User("b")];

        Assert.Equal(0, HistoryHandoff.SupplyStartIndex(history));
    }

    [Fact]
    public void SupplyStart_PointsAtTheNoteItself()
    {
        List<ChatMessage> history = [User("a"), User("b"), HistoryHandoff.CreateNote("交接"), User("c")];

        int start = HistoryHandoff.SupplyStartIndex(history);

        Assert.Equal(2, start);
        Assert.True(HistoryHandoff.IsNote(history[start])); //文档本身必须在供给区间内
    }

    [Fact]
    public void SupplyStart_UsesTheLatestNoteWhenCompactedTwice()
    {
        List<ChatMessage> history =
        [
            User("a"), HistoryHandoff.CreateNote("第一次"), User("b"),
            HistoryHandoff.CreateNote("第二次"), User("c"),
        ];

        Assert.Equal(3, HistoryHandoff.SupplyStartIndex(history));
    }

    /// <summary>
    /// 交接文档自己的篇幅上限：它写完就是下一段历史的起点，太长等于压缩没做，
    /// 极端情况下一写完又过水位，陷入「压缩→还是满→再压缩」的循环。
    /// </summary>
    [Fact]
    public void NoteCharLimit_ScalesWithContextAndStaysClamped()
    {
        int small = HistoryHandoff.NoteCharLimitFor(4096);
        int mid = HistoryHandoff.NoteCharLimitFor(128_000);
        int huge = HistoryHandoff.NoteCharLimitFor(1_048_576);

        Assert.True(small < mid); //小上下文给的篇幅也要小,否则交接文档自己就把窗口占满了
        Assert.Equal(mid, huge); //到顶后不再涨:再长的交接文档也没有价值
        Assert.True(small > 0);
    }

    [Fact]
    public void Cap_LeavesReasonableOutputAloneAndCutsRunaways()
    {
        Assert.Equal("短文", HistoryHandoff.Cap("短文", 100));

        string runaway = new('字', 1000);
        string capped = HistoryHandoff.Cap(runaway, 100);

        Assert.True(capped.Length < runaway.Length);
        Assert.Contains("truncated", capped);
    }

    [Fact]
    public void Cap_GivesTheModelSomeSlackBeforeCutting()
    {
        //稍微超一点不动刀:切在句子中间比让它多写两行更糟
        string slightlyOver = new('字', 120);

        Assert.Equal(slightlyOver, HistoryHandoff.Cap(slightlyOver, 100));
    }

    [Fact]
    public async Task WriteAsync_TellsTheModelItsCharacterBudget()
    {
        StubChatClient client = new("正文");

        await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000, cancellationToken: TestContext.Current.CancellationToken);

        string instruction = client.Seen[^1].Text;
        Assert.Contains(HistoryHandoff.NoteCharLimitFor(128_000).ToString(), instruction);
    }

    /// <summary>上一份交接的附加段会随历史一起交给模型(剔掉它会让前缀缓存整段作废),只能靠指令叫它别照抄</summary>
    [Fact]
    public async Task WriteAsync_TellsTheModelNotToCopyTheGeneratedAppendix()
    {
        StubChatClient client = new("正文");

        await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Contains(HistoryHandoff.AppendixHeading, client.Seen[^1].Text);
    }

    [Fact]
    public async Task WriteAsync_ReturnsNullOnEmptyOutputInsteadOfWritingABlankNote()
    {
        StubChatClient client = new(" \n ", new UsageDetails { InputTokenCount = 900, OutputTokenCount = 3 });

        HandoffReply reply = await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reply.Text);
        Assert.Equal(900, Assert.Single(reply.Usages).InputTokenCount); //没写出正文也照样花了钱,用量照给
    }

    [Fact]
    public async Task WriteAsync_SwallowsFailuresSoTheTurnStillEnds()
    {
        ThrowingChatClient client = new();

        HandoffReply reply = await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Null(reply.Text);
        Assert.Empty(reply.Usages);
    }

    /// <summary>
    /// 第一发不禁工具：tool_choice 写成 none 请求就与常规请求不同形，前缀缓存跟着断。
    /// 模型真去调了工具才禁掉重发一次，两发的用量逐发交回
    /// </summary>
    [Fact]
    public async Task WriteAsync_ForbidsToolsOnlyWhenTheModelReachesForOne()
    {
        ToolReachingChatClient client = new();

        HandoffReply reply = await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([false, true], client.Forbidden);
        Assert.Equal("交接正文", reply.Text);
        Assert.Equal(2, reply.Usages.Count);
    }

    [Fact]
    public async Task WriteAsync_AppendsTheInstructionAfterTheHistory()
    {
        StubChatClient client = new("交接正文");

        string? note = (await HistoryHandoff.WriteAsync(client, [User("聊天内容")], null, 128_000, cancellationToken: TestContext.Current.CancellationToken)).Text;

        Assert.Equal("交接正文", note);
        Assert.Equal(2, client.Seen.Count); //历史 + 指令
        Assert.Equal("聊天内容", client.Seen[0].Text);
        Assert.Null(client.SeenOptions?.Tools); //没给选项时不凭空造工具
    }

    [Fact]
    public async Task WriteAsync_CarriesUserExtraInstructionsIntoTheInstructionMessage()
    {
        // /compact 后跟的文字要随写文档的请求交给模型,让它在交接文档里照顾到
        StubChatClient client = new("交接正文");

        await HistoryHandoff.WriteAsync(client, [User("聊天内容")], null, 128_000, "别忘了记录临时结论", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(2, client.Seen.Count); //仍是一条历史一条指令,结构不变
        Assert.Contains("别忘了记录临时结论", client.Seen[^1].Text);
    }

    [Fact]
    public async Task WriteAsync_IgnoresWhitespaceOnlyExtraInstructions()
    {
        //纯空白不当成额外指示:指令与从前逐字一致,不污染模型
        StubChatClient client = new("交接正文");

        await HistoryHandoff.WriteAsync(client, [User("聊天内容")], null, 128_000, "   ", cancellationToken: TestContext.Current.CancellationToken);

        Assert.DoesNotContain("补充指示", client.Seen[^1].Text);
    }

    private sealed class StubChatClient(string reply, UsageDetails? usage = null) : IChatClient
    {
        public List<ChatMessage> Seen { get; } = [];
        public ChatOptions? SeenOptions { get; private set; }

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Seen.AddRange(messages);
            SeenOptions = options;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply)) { Usage = usage });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>第一发回一个工具调用，第二发回正文；记下每一发是否禁了工具</summary>
    private sealed class ToolReachingChatClient : IChatClient
    {
        public List<bool> Forbidden { get; } = [];

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            Forbidden.Add(LlmRequestContext.ForbidToolCalls);
            ChatMessage message = Forbidden.Count == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")])
                : new ChatMessage(ChatRole.Assistant, "交接正文");
            return Task.FromResult(new ChatResponse(message) { Usage = new UsageDetails { InputTokenCount = 100 } });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class ThrowingChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new HttpRequestException("429");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
