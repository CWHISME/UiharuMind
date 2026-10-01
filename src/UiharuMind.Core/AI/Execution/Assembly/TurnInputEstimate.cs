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
    private const double MaxCalibration = 4; //再高多半是测错了(或服务端把别的也算了进来),封顶免得一发异常就把压缩提得很早
    private const double CalibrationBand = 0.05; //新测值偏离不到这么多就不动,见 Calibrate

    private Func<int>? _fixedOverhead;
    private string? _calibratedModel; //系数属于哪个模型

    /// <summary>
    /// 最近一次压缩判定时的历史 token 估算。由 <c>HistoryCompaction</c> 的触发条件写入——
    /// 那里本就要算一遍，顺手记下等于零成本。
    /// </summary>
    public long LastHistory { get; internal set; }

    /// <summary>
    /// 同一次压缩判定里、压缩动手<b>之前</b>的历史估算（原始历史）。由折叠的触发条件写入——
    /// 它是每次压缩问的第一个条件，此刻还一组没排除。没压时与 <see cref="LastHistory"/> 相等
    /// </summary>
    public long LastRawHistory { get; internal set; }

    /// <summary>最近一次请求发出前被折叠或截断压掉的历史估算；没压时为 0</summary>
    public long CompactedHistory => Math.Max(0, LastRawHistory - LastHistory);

    /// <summary>
    /// 每轮固定开销（系统提示 + 工具定义）。未绑定时为 0，等于退回「不扣固定开销」的旧行为。
    ///
    /// ⚠️ 普通角色（纯提示词）就走这条：它们不登记工具与提示分段，能力快照恒为空。
    /// 它们的系统提示确实也占位，但相对预算小得多，而按档位补齐这笔账是另一件事。
    /// </summary>
    public int FixedOverhead => _fixedOverhead?.Invoke() ?? 0;

    /// <summary>我们估的本轮输入合计，即<b>有效占用</b>取大的那一侧</summary>
    public long Total => FixedOverhead + LastHistory;

    /// <summary>
    /// 校准系数（≥ 1）：最近一次量过的请求里，服务端报的输入 ÷ 我们估的输入。
    /// 折叠与截断判定时乘在估算上；这里记的估算本身不乘——否则下一次拿它去比，系数会自己往 1 收。见 <see cref="Calibrate"/>
    /// </summary>
    public double Calibration { get; private set; } = 1;

    /// <summary>
    /// 装配末尾绑定到句柄。
    /// 传委托而不是直接取数，是为了让分词保持惰性：绑定时不算，第一次读才算。
    /// </summary>
    /// <param name="handle">本次装配产出的句柄</param>
    public void BindTo(AgentHandle handle)
    {
        _fixedOverhead = () => handle.Capabilities.EstimatedTokens;
    }

    /// <summary>
    /// 按服务端报的输入校准。
    ///
    /// 各家的分词器与提示模板我们拿不到，我们的估算偏离多少全看模型；唯一准的是服务端每次报的输入，
    /// 而那一发装了什么我们清楚。下一发 ≈ 这一发 + 新增的几条，乘上这一发的比值，误差就只剩新增那截的估算。
    /// <list type="bullet">
    /// <item><b>只往上调</b>：少报的服务端（GLM 不计工具定义，比值 0.48）取 1，不把估算往下拖——晚压的代价远大于早压，见 ADR 0009。</item>
    /// <item><b>带死区</b>：偏离不到 5% 不动。折叠与截断按台阶走，同一级里压哪些组不变靠的是要腾的量不变；系数每发微调，前缀就又不稳了。</item>
    /// <item><b>没量过不算</b>：历史估算为 0 的那一发（新会话、交接后的首发，框架跳过了压缩判定）只估了固定开销，
    /// 服务端却连交接文档一起算，比值虚高。</item>
    /// <item><b>换模型重来</b>：系数属于那一个模型。</item>
    /// </list>
    /// </summary>
    /// <param name="model">这一发用的模型</param>
    /// <param name="reported">服务端报的输入 token</param>
    public void Calibrate(string? model, long reported)
    {
        UseModel(model);
        long ours = Total;
        if (reported <= 0 || LastHistory <= 0 || ours <= 0) return;

        double measured = Math.Clamp((double)reported / ours, 1, MaxCalibration);
        if (Math.Abs(measured - Calibration) > Calibration * CalibrationBand) Calibration = measured;
    }

    /// <summary>
    /// 声明接下来用哪个模型；与上次校准的不是同一个就把系数归 1。轮次开头调：
    /// agent 不随换模型重建，不在发出第一发之前归位的话，那一发会按上一个模型的系数压
    /// </summary>
    /// <param name="model">模型名</param>
    public void UseModel(string? model)
    {
        if (model == _calibratedModel) return;
        _calibratedModel = model;
        Calibration = 1;
    }

    /// <summary>
    /// 忘掉历史估算，回到「还没压缩判定过」的状态（与新会话、句柄重建后的首个请求一致）。
    /// 交接文档写成之后调用：此前的估算描述的是已被它替换的那段历史
    /// </summary>
    public void ForgetHistory()
    {
        LastHistory = 0;
        LastRawHistory = 0;
    }

    /// <summary>
    /// 不绑句柄、固定开销取定值的一份。给旁路请求单独压一次用（见 <c>HistorySupply.ForSideRequestAsync</c>）：
    /// 压缩会回写这里的历史估算，与 agent 那份共用的话，旁路一压就把界面与交接水位读的数改掉了
    /// </summary>
    /// <param name="fixedOverhead">固定开销</param>
    /// <returns>独立的一份估算</returns>
    internal static TurnInputEstimate Detached(int fixedOverhead)
    {
        return new TurnInputEstimate { _fixedOverhead = () => fixedOverhead };
    }
}
