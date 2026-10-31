/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Runtime.CompilerServices;
using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 发送前的逐次改写链：记忆清单并进正文 → 压缩 → 识图投影。
/// <b>常规请求与旁路请求（写记忆压缩、群背景摘要）共用这一条</b>，顺序即语义——
/// 清单在压缩外面（压缩按并进清单后的真实大小算），识图投影在压缩里面（工具组被折叠，图就跟着不发）。
///
/// 为什么必须共用一处：旁路请求绕开了常规那条客户端链（插话、审批、逐次落盘都是有状态层，不能跟着用），
/// 于是同一套改写得有人再做一遍。照着重写就会漏，而漏了不会报错——只是请求从那一层起岔开，
/// 从岔口往后整段前缀缓存作废。实测写记忆压缩那一发 334721 只命中 80896：
/// 没做识图投影，36 条看图消息整条消失，第 62 条起 428 条消息全部冷启动。
/// </summary>
internal static class HistoryRewriteChain
{
    /// <summary>
    /// 按装配事实造一条改写链
    /// </summary>
    /// <param name="transport">链末端的传输端</param>
    /// <param name="plan">装配计划（路径口径与附图开关都从它取）</param>
    /// <param name="compaction">压缩策略；null 表示这一发不压</param>
    /// <returns>改写好、可以直接发的客户端</returns>
    public static IChatClient Build(IChatClient transport, AgentAssemblyPlan plan, CompactionStrategy? compaction)
    {
        return Build(transport, plan.ReadProjectsImages ? plan.CreatePathResolver(null) : null,
            plan.ReadAttachesImages, compaction);
    }

    /// <summary>
    /// 按显式的投影口径造一条改写链。给测试与不装配计划的场合用——装配那条路一律走计划那一份，
    /// 免得路径口径两处各说一套
    /// </summary>
    /// <param name="transport">链末端的传输端</param>
    /// <param name="projectionPaths">识图投影的路径口径；null 表示不做识图投影</param>
    /// <param name="attachImages">识图投影是否真的附图（模型看不了图时只发占位）</param>
    /// <param name="compaction">压缩策略；null 表示这一发不压</param>
    /// <returns>改写好、可以直接发的客户端</returns>
    public static IChatClient Build(IChatClient transport, AgentPathResolver? projectionPaths, bool attachImages,
        CompactionStrategy? compaction)
    {
        IChatClient chain = transport;
        // 识图开关开着才挂投影：模型自己看不了图时发占位而不是整条不发，否则历史里那些看图回合会断在半空
        if (projectionPaths != null) chain = new ImagePreviewProjectingChatClient(chain, projectionPaths, attachImages);

        return new MemoryIndexProjectingChatClient(ApplyCompaction(chain, compaction));
    }

    /// <summary>
    /// 把压缩策略挂到链上（落点见 <c>AgentAssembler.MoveCompactionToLeaf</c> 的注释）
    /// </summary>
    /// <param name="client">链</param>
    /// <param name="compaction">压缩策略；null 表示不压</param>
    /// <returns>挂好压缩的链</returns>
    public static IChatClient ApplyCompaction(IChatClient client, CompactionStrategy? compaction)
    {
        return compaction == null ? client : client.AsBuilder().UseChatReducer(compaction.AsChatReducer()).Build();
    }

    /// <summary>
    /// 旁路请求的改写入口：拿同一条链跑一遍，把链末端收到的消息截下来。
    ///
    /// 压缩换成一份新的：台阶闭包与输入估算都是一次压缩一份，复用装配里那份会与在跑的那一轮互相踩
    /// （<see cref="HistoryCompaction.Create"/> 的注释）。
    /// </summary>
    /// <param name="plan">装配计划</param>
    /// <returns>改写入口</returns>
    public static SideRequestRewriter ForSideRequest(AgentAssemblyPlan plan) =>
        ForSideRequest((transport, compaction) => Build(transport, plan, compaction));

    /// <summary>
    /// 旁路请求的改写入口，链由调用方给（测试与不装配计划的场合）
    /// </summary>
    /// <param name="build">链的造法</param>
    /// <returns>改写入口</returns>
    public static SideRequestRewriter ForSideRequest(Func<IChatClient, CompactionStrategy?, IChatClient> build) =>
        async (supplied, contextLength, fixedOverhead, cancellationToken) =>
        {
            MessageCapturingChatClient capture = new();
            CompactionStrategy compaction =
                HistoryCompaction.Create(() => contextLength, TurnInputEstimate.Detached(fixedOverhead));
            // 走流式：常规请求就是流式出去的，压缩与投影在两条路上的落点必须一致
            await foreach (ChatResponseUpdate _ in build(capture, compaction)
                               .GetStreamingResponseAsync(supplied, null, cancellationToken)
                               .ConfigureAwait(false))
            {
            }

            return capture.Captured;
        };
}

/// <summary>
/// 把「按供给区间取好的历史」改写成旁路请求真正要发的那一份。
/// 由装配处造好挂在句柄上（见 <see cref="HistoryRewriteChain.ForSideRequest(AgentAssemblyPlan)"/>），
/// 旁路请求直接调它——改写口径因此只有一处，不会漏层
/// </summary>
/// <param name="supplied">供给区间内的历史（<see cref="HistorySupply.From"/> 的结果）</param>
/// <param name="contextLength">当前模型的上下文上限；0 表示未知，不压缩</param>
/// <param name="fixedOverhead">每轮固定开销（系统提示 + 工具定义）</param>
/// <param name="cancellationToken">取消标记</param>
/// <returns>可以直接发出去的历史</returns>
public delegate Task<IReadOnlyList<ChatMessage>> SideRequestRewriter(IReadOnlyList<ChatMessage> supplied,
    int contextLength, int fixedOverhead, CancellationToken cancellationToken);

/// <summary>
/// 链末端的捕获端：把收到的消息记下来，不真的发请求。旁路请求借它拿到「常规请求会发的那一份」，
/// 于是改写不必再写第二遍——链上多挂一层，这里自动跟着多一层
/// </summary>
internal sealed class MessageCapturingChatClient : IChatClient
{
    /// <summary>链末端最后收到的消息；还没跑过链时为空</summary>
    public IReadOnlyList<ChatMessage> Captured { get; private set; } = [];

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        Captured = messages.ToList();
        return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        Captured = messages.ToList();
        await Task.CompletedTask.ConfigureAwait(false); //捕获完就结束,一条更新都不发
        yield break;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose()
    {
    }
}
