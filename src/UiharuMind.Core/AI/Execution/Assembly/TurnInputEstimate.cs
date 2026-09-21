/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Assembly;

/// <summary>
/// 我们自己对「本轮请求的输入有多大」的估算：<b>固定开销 + 历史</b>。
///
/// 存在的理由是<b>服务端报的那个数不一定可信</b>：实测 GLM4-Flash 的 <c>prompt_tokens</c>
/// 不含工具定义，少报近一半。交接文档水位若只信它，在那类服务端上就<b>永远不触发</b>——
/// 三条水位里唯一会调模型、也唯一能保住上下文的那条形同虚设。见 ADR 0009。
///
/// 两半来自不同的时刻，所以要有这么个盒子接住：
/// <list type="bullet">
/// <item>固定开销要等装配完成才算得出来（它含系统提示，而提示是装配现场拼的），
/// 而压缩策略在 <see cref="AgentAssemblyPlan.Resolve"/> 里就要构造好；</item>
/// <item>历史估算由压缩策略的触发条件在每次请求前顺手写下，不额外分词。</item>
/// </list>
/// </summary>
public sealed class TurnInputEstimate
{
    private Func<int>? _fixedOverhead;

    /// <summary>
    /// 最近一次压缩判定时的历史 token 估算。由 <c>HistoryCompaction</c> 的触发条件写入——
    /// 那里本就要算一遍，顺手记下等于零成本。
    /// </summary>
    public long LastHistory { get; internal set; }

    /// <summary>
    /// 每轮固定开销（系统提示 + 工具定义）。未绑定时为 0，等于退回「不扣固定开销」的旧行为。
    ///
    /// ⚠️ 纯提示词档（扮演 / 工具人）就走这条：它们不登记工具与提示分段，能力快照恒为空。
    /// 它们的系统提示确实也占位，但相对预算小得多，而按档位补齐这笔账是另一件事。
    /// </summary>
    public int FixedOverhead => _fixedOverhead?.Invoke() ?? 0;

    /// <summary>我们估的本轮输入合计，即<b>有效占用</b>取大的那一侧</summary>
    public long Total => FixedOverhead + LastHistory;

    /// <summary>
    /// 装配末尾绑定到句柄。
    /// 传委托而不是直接取数，是为了让分词保持惰性：绑定时不算，第一次读才算。
    /// </summary>
    /// <param name="handle">本次装配产出的句柄</param>
    public void BindTo(AgentHandle handle)
    {
        _fixedOverhead = () => handle.Capabilities.EstimatedTokens;
    }
}
