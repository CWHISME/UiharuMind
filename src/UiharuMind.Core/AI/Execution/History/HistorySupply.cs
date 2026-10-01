/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI.Compaction;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.AI.Execution.History;

/// <summary>
/// 供给给模型的那一份历史。常规请求与旁路请求（写交接文档、群背景摘要）共用这一处口径：
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
    /// 旁路请求的供给：常规供给，再按常规请求的同一套压缩策略压一遍。
    ///
    /// 旁路请求走的是不带压缩的原始客户端。不先压这一道，发出去的就是原始历史：前缀在第一组折叠处就与
    /// 已缓存的那份岔开，而这一发恰好是占用最高时最大的一次请求；原始历史还可能比窗口本身还大，请求直接被拒。
    /// 折叠只看原始大小落在哪一级台阶，所以同一份历史压出来与常规请求逐字相同；多出的最后那条回复只在末尾。
    ///
    /// 每次新建一份策略而不复用 agent 那份：台阶闭包与输入估算都是一次压缩一份，复用会与在跑的请求互相踩。
    /// </summary>
    /// <param name="full">完整历史</param>
    /// <param name="options">会话装配好的选项；没挂工具即普通对话形态（装配侧那个开关旁路请求拿不到）</param>
    /// <param name="contextLength">当前模型的上下文上限；未知（0）时不压缩</param>
    /// <param name="fixedOverhead">每轮固定开销（系统提示 + 工具定义）</param>
    /// <param name="cancellationToken">取消标记</param>
    /// <returns>要交给旁路请求的历史</returns>
    public static async Task<IReadOnlyList<ChatMessage>> ForSideRequestAsync(IReadOnlyList<ChatMessage> full,
        ChatOptions? options, int contextLength, int fixedOverhead, CancellationToken cancellationToken)
    {
        IReadOnlyList<ChatMessage> supplied = From(full, promptOnly: options?.Tools is not { Count: > 0 });
        CompactionStrategy compaction =
            HistoryCompaction.Create(() => contextLength, TurnInputEstimate.Detached(fixedOverhead));
        IEnumerable<ChatMessage> compacted = await compaction.AsChatReducer()
            .ReduceAsync(supplied, cancellationToken)
            .ConfigureAwait(false);
        return compacted.ToList();
    }
}
