using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.History;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 旁路请求的改写入口，走装配处同一个工厂（<see cref="HistoryRewriteChain.ForSideRequest(Func{IChatClient, Microsoft.Agents.AI.Compaction.CompactionStrategy, IChatClient})"/>）。
/// 只是没有路径口径与识图投影——那两样要装配计划。
///
/// 测试里别另写一套压缩：要验的是「旁路请求与常规请求同一条链」，自己拼一条就测不到了
/// </summary>
internal static class SideRequestRewriters
{
    /// <summary>不做识图投影的一份</summary>
    public static readonly SideRequestRewriter Plain = HistoryRewriteChain.ForSideRequest(
        (transport, compaction) => HistoryRewriteChain.Build(transport, projectionPaths: null, attachImages: () => false,
            compaction));
}
