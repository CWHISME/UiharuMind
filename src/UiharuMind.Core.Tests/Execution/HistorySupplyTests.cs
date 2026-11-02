using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 供给口径：常规请求与旁路请求（写交接文档、群背景摘要）交给模型的必须是同一份。
/// 旁路请求前缀一岔开，那一发——恰好是占用最高时最大的一次——就整段不中缓存
/// </summary>
public class HistorySupplyTests
{
    private const int ContextLength = 20_000; //额度约 17.5k token,每组工具调用约 300 token

    private static readonly ChatOptions WithTools = new() { Tools = [AIFunctionFactory.Create(() => "x", "Read")] };

    [Fact]
    public void From_StartsFromTheLastHandoff_AndSkipsKnowledge()
    {
        ChatMessage knowledge = new(ChatRole.User, "检索片段");
        knowledge.AdditionalProperties = new AdditionalPropertiesDictionary { [ChatMessageAnnotations.Knowledge] = true };
        ChatMessage handoff = HistoryHandoff.CreateNote("之前聊过的");
        ChatMessage ask = new(ChatRole.User, "接着说");
        List<ChatMessage> history = [new(ChatRole.User, "很早的话"), handoff, knowledge, ask];

        Assert.Equal([handoff, ask], HistorySupply.From(history, promptOnly: false));
    }

    /// <summary>旁路请求拿不到装配侧的形态开关，按选项里挂没挂工具判：没挂就去掉工具内容，否则模型会照着历史再编一个调用</summary>
    [Fact]
    public async Task ForSideRequest_WithoutTools_DropsToolContent()
    {
        ChatMessage call = new(ChatRole.Assistant, [new FunctionCallContent("c1", "Read")]);
        ChatMessage result = new(ChatRole.Tool, [new FunctionResultContent("c1", "内容")]);
        ChatMessage answer = new(ChatRole.Assistant, "看过了");
        List<ChatMessage> history = [new(ChatRole.User, "看一下"), call, result, answer];
        CancellationToken token = TestContext.Current.CancellationToken;

        Assert.Equal(2, (await HistorySupply.ForSideRequestAsync(history, null, SideRequestRewriters.Plain, ContextLength, 0, token)).Count);
        Assert.Equal(4, (await HistorySupply.ForSideRequestAsync(history, WithTools, SideRequestRewriters.Plain, ContextLength, 0, token)).Count);
    }

    /// <summary>
    /// 一轮里折叠动过手之后写交接：交接请求必须原样接在最后一次常规请求后面，只多出那条最终回复。
    /// 从前它发原始历史，前缀在第一组折叠处就岔开
    /// </summary>
    [Fact]
    public async Task ForSideRequest_ExtendsTheLastRegularRequest_SoTheHandoffHitsTheCache()
    {
        List<ChatMessage> history = [new(ChatRole.User, "把这些文件都看一遍")];
        for (int i = 0; i < 55; i++) HistoryCompactionTests.AddToolGroup(history, i);
        CancellationToken token = TestContext.Current.CancellationToken;

        // 最后一次常规请求:不含它自己产出的那条回复
        IChatReducer agentSide = HistoryCompaction.Create(() => ContextLength, new TurnInputEstimate()).AsChatReducer();
        List<ChatMessage> regular =
            (await agentSide.ReduceAsync(HistorySupply.From(history, promptOnly: false), token)).ToList();
        Assert.True(HistoryCompactionTests.Folded(regular) > 0, "这一轮该折过了，否则测的不是折叠之后的交接");

        history.Add(new ChatMessage(ChatRole.Assistant, "都看完了"));
        IReadOnlyList<ChatMessage> handoff =
            await HistorySupply.ForSideRequestAsync(history, WithTools, SideRequestRewriters.Plain, ContextLength, 0, token);

        Assert.Equal(regular.Select(HistoryCompactionTests.Signature),
            handoff.Take(regular.Count).Select(HistoryCompactionTests.Signature));
        Assert.Equal(regular.Count + 1, handoff.Count);
    }

    /// <summary>原始历史早已超出窗口（折叠一直在替它腾地方）时，交接请求仍得发得出去</summary>
    [Fact]
    public async Task ForSideRequest_FitsTheWindow_EvenWhenTheRawHistoryDoesNot()
    {
        List<ChatMessage> history = [new(ChatRole.User, "把这些文件都看一遍")];
        for (int i = 0; i < 200; i++) HistoryCompactionTests.AddToolGroup(history, i);
        int quota = HistoryCompaction.HistoryQuotaFor(ContextLength, 0);
        CancellationToken token = TestContext.Current.CancellationToken;
        Assert.True(await HistoryCompactionTests.TokensAsync(history, token) > ContextLength, "原始历史得先超窗，否则测的不是这件事");

        IReadOnlyList<ChatMessage> handoff =
            await HistorySupply.ForSideRequestAsync(history, WithTools, SideRequestRewriters.Plain, ContextLength, 0, token);

        long sent = await HistoryCompactionTests.TokensAsync(handoff, token);
        Assert.True(sent <= quota * HistoryCompaction.TruncationThreshold, $"压完仍有 {sent}");
    }

    /// <summary>
    /// 旁路请求跑的是常规请求那条链，不是照着重写的一份——链上新挂一层，旁路必须跟着有。
    /// 识图投影就是这么漏的：写记忆压缩那一发少了 36 条看图消息，从第 62 条起 428 条消息的前缀全部冷启动
    /// </summary>
    [Fact]
    public async Task ForSideRequest_RunsTheSameChain_SoANewLayerIsCarried()
    {
        SideRequestRewriter rewriter = HistoryRewriteChain.ForSideRequest((transport, compaction) =>
            new MarkerChatClient(HistoryRewriteChain.Build(transport, projectionPaths: null, attachImages: () => false,
                compaction)));

        IReadOnlyList<ChatMessage> supplied = await rewriter([new ChatMessage(ChatRole.User, "历史")],
            ContextLength, 0, TestContext.Current.CancellationToken);

        Assert.Equal(MarkerChatClient.Marker, supplied[^1].Text);
    }

    /// <summary>链上的一层：往消息末尾插一条标记，用来验旁路请求确实走过这条链</summary>
    private sealed class MarkerChatClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        internal const string Marker = "链上新挂的那一层";

        public override Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetResponseAsync(Mark(messages), options, cancellationToken);

        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            base.GetStreamingResponseAsync(Mark(messages), options, cancellationToken);

        private static IEnumerable<ChatMessage> Mark(IEnumerable<ChatMessage> messages) =>
            messages.Append(new ChatMessage(ChatRole.User, Marker));
    }
}
