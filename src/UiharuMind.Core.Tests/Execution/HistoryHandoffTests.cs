using System.Runtime.CompilerServices;
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
    

    [Fact]
    public void LastAssistantText_TakesTheWholeBodyOfTheLastAssistantMessage()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.User, "a"),
            new(ChatRole.Assistant, "先看 a.cs。\n\n再看 b.cs。它改的是入口。"),
            new(ChatRole.User, "b"),
        ];

        Assert.Equal("先看 a.cs。\n\n再看 b.cs。它改的是入口。", HistoryHandoff.LastAssistantText(history));
    }

    /// <summary>
    /// 「最后一句」指的是上一轮最后发出的<b>整条正文</b>，不是按标点切出来的那一句。
    /// 曾经按标点取，实测切出「它小、独立、有判据，我可以直接做完给你看同机位前后图」——
    /// 没有主语的碎片，读起来像被截断
    /// </summary>
    [Fact]
    public void LastAssistantText_IsNotCutDownToTheFinalSentence()
    {
        const string message = """
                               我已经做了一件清理，两个理由都说过了。

                               1. 认不认删 `feet` 配方这条内容决定？
                               2. 要不要现在就上第 0 步？它小、独立、有判据，我可以直接做完给你看同机位前后图 (｡･ω･)ﻭ
                               """;

        Assert.Equal(message, HistoryHandoff.LastAssistantText([new ChatMessage(ChatRole.Assistant, message)]));
    }

    [Fact]
    public void LastAssistantText_SkipsNotesNarrationAndTextlessMessages()
    {
        List<ChatMessage> history =
        [
            new(ChatRole.Assistant, "真正的上一轮正文。"),
            HistoryHandoff.CreateNote("上一份记忆压缩"),
            new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]), //纯工具调用没有正文
        ];

        Assert.Equal("真正的上一轮正文。", HistoryHandoff.LastAssistantText(history));
    }

    [Fact]
    public void LastAssistantText_WithoutAnyAssistantText_ReturnsNull()
    {
        ChatMessage narration = new(ChatRole.Assistant, "开场白")
        {
            AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Narration] = "x" },
        };

        Assert.Null(HistoryHandoff.LastAssistantText([narration, HistoryHandoff.CreateNote("上一份记忆压缩")]));
        Assert.Null(HistoryHandoff.LastAssistantText([new(ChatRole.User, "只有用户")]));
        Assert.Null(HistoryHandoff.LastAssistantText([]));
    }

    [Fact]
    public void LastAssistantText_KeepsVersionNumbersAndTrailingPunctuation()
    {
        Assert.Equal("first. next.", HistoryHandoff.LastAssistantText([new ChatMessage(ChatRole.Assistant, "first. next.")]));
        Assert.Equal("版本 v1.2.3", HistoryHandoff.LastAssistantText([new ChatMessage(ChatRole.Assistant, "版本 v1.2.3")]));
        Assert.Equal("你好。好的", HistoryHandoff.LastAssistantText([new ChatMessage(ChatRole.Assistant, "你好。好的")]));
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

    /// <summary>
    /// 必须走流式：请求体里的 stream / stream_options 也算「请求形状」，用 GetResponseAsync 发就少这两处，
    /// 网关的前缀缓存认不出这是同一段前缀——实测同一段纯追加的 42 条消息，常规轮次 98% 命中，这一发 0
    /// </summary>
    [Fact]
    public async Task WriteAsync_GoesThroughTheStreamingPath()
    {
        StreamingOnlyChatClient client = new("交接正文");

        HandoffReply reply = await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("交接正文", reply.Text);
        Assert.Equal(100, reply.Usages[0].InputTokenCount);
    }

    /// <summary>
    /// 写到一半的数要报给界面：整份文档十几秒起步，占位卡只有一个转圈看不出它在动。
    /// 只报「已写出多少」，报得单调递增；准确的那个数由收尾的 usage 给
    /// </summary>
    [Fact]
    public async Task WriteAsync_ReportsWrittenTokensWhileStreaming()
    {
        ChunkedChatClient client = new(Enumerable.Repeat("记忆压缩的正文，一句一句地写出来。", 20).ToArray());
        List<int> progress = [];

        HandoffReply reply = await HistoryHandoff.WriteAsync(client, [User("a")], null, 128_000,
            onProgress: progress.Add, cancellationToken: TestContext.Current.CancellationToken);

        Assert.NotNull(reply.Text);
        Assert.NotEmpty(progress);
        Assert.True(progress[^1] > 0, "一次都没报出数");
        Assert.Equal(progress.Order(), progress); //只能往上走
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

    /// <summary>
    /// 把假响应按流式喂出去。写记忆压缩现在走流式——请求体的 stream / stream_options 也是「请求形状」的一部分，
    /// 非流式那一发网关的前缀缓存认不出来（见 HistoryHandoff.SendAsync）
    /// </summary>
    private static async IAsyncEnumerable<ChatResponseUpdate> AsUpdates(Task<ChatResponse> response)
    {
        foreach (ChatResponseUpdate update in (await response.ConfigureAwait(false)).ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    /// <summary>分块吐字的客户端：用来验写文档过程中的进度上报</summary>
    private sealed class ChunkedChatClient(params string[] chunks) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("写记忆压缩必须走流式");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            foreach (string chunk in chunks)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunk);
                await Task.Yield(); //真的分多次到达,否则测不出「边流边报」
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    /// <summary>只实现流式的客户端：非流式一发就炸，用来钉住「写记忆压缩必须走流式」</summary>
    private sealed class StreamingOnlyChatClient(string reply) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("写记忆压缩必须走流式");

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            AsUpdates(Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, reply))
            {
                Usage = new UsageDetails { InputTokenCount = 100 },
            }));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
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
            AsUpdates(GetResponseAsync(messages, options, cancellationToken));

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
            Forbidden.Add(LlmRequestScope.Current.ForbidToolCalls);
            ChatMessage message = Forbidden.Count == 1
                ? new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")])
                : new ChatMessage(ChatRole.Assistant, "交接正文");
            return Task.FromResult(new ChatResponse(message) { Usage = new UsageDetails { InputTokenCount = 100 } });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            AsUpdates(GetResponseAsync(messages, options, cancellationToken));

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
            AsUpdates(GetResponseAsync(messages, options, cancellationToken));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
