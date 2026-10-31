/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 供给给模型的那一份历史，以及发它的客户端。常规请求与旁路请求（写交接文档、群背景摘要）共用这一处口径：
/// 旁路请求的前缀要与常规请求逐字一致才中得了服务端缓存，哪一处各写一遍都会悄悄岔开。
/// </summary>
internal static class HistorySupply
{
    /// <summary>
    /// 常规供给：从最后一份交接文档起，不带知识库片段；普通对话形态再去掉工具内容。
    ///
    /// 知识库检索片段「存而不供」：落盘只为界面回溯，回灌给模型就会逐轮累积过期上下文。
    /// 每轮的片段由 <c>MemoryContextProvider</c> 按当前提问重新检索并注入，模型要看的永远是新的那份
    /// </summary>
    /// <param name="full">完整历史</param>
    /// <param name="promptOnly">是否普通对话形态（不挂工具），见 <see cref="PromptOnlyHistory"/></param>
    /// <returns>供给区间</returns>
    public static IReadOnlyList<ChatMessage> From(IReadOnlyList<ChatMessage> full, bool promptOnly)
    {
        IReadOnlyList<ChatMessage> supplied = full
            .Skip(HistoryHandoff.SupplyStartIndex(full))
            .Where(x => !ChatMessageAnnotations.IsKnowledge(x))
            .ToList();
        return promptOnly ? PromptOnlyHistory.StripToolContents(supplied) : supplied;
    }

    /// <summary>
    /// 旁路请求的客户端：与常规请求同一条路径，而不是直接用会话模型的底层客户端。
    /// 请求体的逐模型改写（思考模式下按 tool_call 回填 reasoning_content 等）都在 <see cref="LazyChatClient"/> 里，
    /// 绕过它的话，第一条带工具调用的助手消息就与已缓存的那份岔开
    /// </summary>
    /// <param name="session">发请求的会话</param>
    /// <returns>客户端；不持有底层客户端，用完不必释放</returns>
    public static IChatClient ClientFor(ChatSession session)
    {
        return new LazyChatClient(() => session.ChatModelRunningData, session.CacheAffinity);
    }

    /// <summary>
    /// 旁路请求的供给：常规供给，再按常规请求的同一套逐次改写（记忆清单 → 压缩 → 识图投影）过一遍。
    ///
    /// 改写本身不在这里写：那是 <see cref="HistoryRewriteChain"/>，与常规请求共用同一条链。
    /// 从前在这里照着重写，漏了识图投影——写记忆压缩那一发 334721 只命中 80896，
    /// 36 条看图消息整条消失，从第 62 条起 428 条消息的前缀全部冷启动。
    ///
    /// 每次都由链现造一份压缩：台阶闭包与输入估算都是一次压缩一份，复用 agent 那份会与在跑的请求互相踩
    /// </summary>
    /// <param name="full">完整历史</param>
    /// <param name="options">会话装配好的选项；没挂工具即普通对话形态（装配侧那个开关旁路请求拿不到）</param>
    /// <param name="rewriter">旁路请求的改写入口，取自句柄；不走 harness 的形态为 null</param>
    /// <param name="contextLength">当前模型的上下文上限；未知（0）时不压缩</param>
    /// <param name="fixedOverhead">每轮固定开销（系统提示 + 工具定义）</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>要交给旁路请求的历史</returns>
    public static async Task<IReadOnlyList<ChatMessage>> ForSideRequestAsync(IReadOnlyList<ChatMessage> full,
        ChatOptions? options, SideRequestRewriter? rewriter, int contextLength, int fixedOverhead,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> supplied = From(full, promptOnly: options?.Tools is not { Count: > 0 });
        // 没有装配产物就没有链：原样发，压不了也投影不了
        if (rewriter == null) return supplied;
        return await rewriter(supplied, contextLength, fixedOverhead, cancellationToken).ConfigureAwait(false);
    }
}
