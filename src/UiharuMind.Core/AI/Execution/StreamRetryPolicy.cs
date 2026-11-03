using UiharuMind.Core.AI.Net.Errors;

namespace UiharuMind.Core.AI.Execution;

/// <summary>这一次失败之后要做什么——动作本身已区分「要不要先自愈」，不必再回到失败种类上去分派</summary>
internal enum EStreamRetryAction
{
    /// <summary>就地失败，交给上层</summary>
    GiveUp,

    /// <summary>把 max_tokens 回退到服务端声明的上限，再整段重发</summary>
    FixMaxTokens,

    /// <summary>记下「历史里每条助手消息都要带思考」，再整段重发</summary>
    LearnReasoningRequirement,

    /// <summary>不退避、也不修任何东西，立刻整段重发（回复退化成空白）</summary>
    RetryImmediately,

    /// <summary>退避后整段重发（流中途被掐断）</summary>
    RetryAfterDelay,
}

/// <summary>失败的种类：只用于诊断与措辞，处置由 <see cref="EStreamRetryAction"/> 表达</summary>
internal enum EStreamFailureKind
{
    /// <summary>服务端拒 max_tokens（400），可把配置回退到它声明的上限</summary>
    MaxTokens,

    /// <summary>服务端要求历史里每条助手消息都带思考（400），可记在这个模型名下</summary>
    ReasoningRequirement,

    /// <summary>回复退化成一片空白</summary>
    Degenerate,

    /// <summary>流中途断了（判据见 <see cref="TransportFault"/>）</summary>
    TransportDrop,

    /// <summary>以上都不是：原样抛给上层</summary>
    Fatal,
}

/// <summary>这一发已经用掉的预算</summary>
/// <param name="Attempt">当前是第几次尝试（从 1 开始）。<b>总数</b>，不是断线次数——自愈与退化重发也占名额</param>
/// <param name="MaxTokensFixed">max_tokens 的配置已经改过一次</param>
/// <param name="ReasoningLearned">「每条助手消息都要带思考」已经记过一次</param>
/// <param name="DegenerateRetries">已经因退化重发过几次</param>
internal readonly record struct StreamRetryBudget(int Attempt, bool MaxTokensFixed, bool ReasoningLearned,
    int DegenerateRetries);

/// <summary>这一次的处置</summary>
/// <param name="Action">要做什么</param>
/// <param name="Delay">退避时长（只有 <see cref="EStreamRetryAction.RetryAfterDelay"/> 用得到）</param>
internal readonly record struct StreamRetryPlan(EStreamRetryAction Action, TimeSpan Delay);

/// <summary>
/// 一次流式失败该不该重发、怎么重发——<b>纯判定</b>：不碰配置、不发请求、不写日志。
///
/// <para>
/// 单独成一处，是因为这四条分支各自都带预算（自愈只做一次、退化与断线分开计、断线共 6 次），
/// 混在重试循环里只能靠「造一个假客户端 + 等满 1 秒退避 + 改全局配置」才测得到；
/// 抽出来以后每条预算都是一次普通函数调用，分支组合也说得清。
/// </para>
/// <para>真正动手（改配置、记要求、退避、写日志）仍在 <see cref="LazyChatClient"/> 里。</para>
/// <para>
/// 与 <c>Net/Transport/RateLimitAwareRetryPolicy</c> 分工：那层管 429/5xx 的 SDK 级重试（请求还没走通），
/// 这层管响应开始之后被掐断、以及 400 这类要改配置才能过的情况，粒度是「整个请求重发」。
/// </para>
/// </summary>
internal static class StreamRetryPolicy
{
    /// <summary>尝试次数上限（含首发）。自愈与退化重发一起吃这个额度，所以它不是「断线几次」</summary>
    public const int MaxAttempts = 6;

    /// <summary>
    /// 退化回复的重发次数上限。预算与断线分开记——它不是网络问题，等一等不会变好；
    /// 同一个模型连续几次都退化的概率不低，重发太多只是白烧 token
    /// </summary>
    public const int MaxDegenerateRetries = 2;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1); //退避基数:1s、2s、4s…
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8); //封顶:连续断线时等太久收益有限

    /// <summary>
    /// 这次失败属于哪一类。顺序即优先级：先认专门的 400（它们带可自愈的信息），
    /// 再认退化，最后才是断流；都不认的是 <see cref="EStreamFailureKind.Fatal"/>
    /// </summary>
    /// <param name="failure">流式枚举抛出的异常</param>
    /// <returns>失败种类</returns>
    public static EStreamFailureKind Classify(Exception failure)
    {
        if (MaxTokensErrorHandler.IsMaxTokensError(failure)) return EStreamFailureKind.MaxTokens;
        if (ReasoningRequirementErrorHandler.IsRequirementError(failure)) return EStreamFailureKind.ReasoningRequirement;
        if (failure is DegeneratedReplyException) return EStreamFailureKind.Degenerate;
        if (TransportFault.IsRetryableDrop(failure)) return EStreamFailureKind.TransportDrop;
        return EStreamFailureKind.Fatal;
    }

    /// <summary>按已经用掉的预算决定这一次的处置</summary>
    /// <param name="kind">失败种类</param>
    /// <param name="budget">这一发已经用掉的预算</param>
    /// <returns>处置计划</returns>
    public static StreamRetryPlan Plan(EStreamFailureKind kind, in StreamRetryBudget budget) => kind switch
    {
        // 自愈各只做一次：记过/改过之后请求体已经不一样了，再被拒就是别的问题
        EStreamFailureKind.MaxTokens => GiveUpOr(budget.MaxTokensFixed, EStreamRetryAction.FixMaxTokens),
        EStreamFailureKind.ReasoningRequirement =>
            GiveUpOr(budget.ReasoningLearned, EStreamRetryAction.LearnReasoningRequirement),

        EStreamFailureKind.Degenerate =>
            GiveUpOr(budget.DegenerateRetries >= MaxDegenerateRetries, EStreamRetryAction.RetryImmediately),

        EStreamFailureKind.TransportDrop => budget.Attempt >= MaxAttempts
            ? new StreamRetryPlan(EStreamRetryAction.GiveUp, TimeSpan.Zero)
            : new StreamRetryPlan(EStreamRetryAction.RetryAfterDelay, Delay(budget.Attempt)),

        _ => new StreamRetryPlan(EStreamRetryAction.GiveUp, TimeSpan.Zero),
    };

    /// <summary>断线退避时长：以固定基数为底的指数退避，封顶 8 秒</summary>
    /// <param name="attempt">当前是第几次尝试（从 1 开始）</param>
    /// <returns>这一次重发前该等多久</returns>
    public static TimeSpan Delay(int attempt)
    {
        double scaled = BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        return TimeSpan.FromSeconds(Math.Min(scaled, MaxDelay.TotalSeconds));
    }

    /// <summary>预算已经用完就收口，否则按给定动作重发（这类动作都不需要退避）</summary>
    /// <param name="exhausted">这类预算是否已经用完</param>
    /// <param name="action">用完时该做的动作</param>
    /// <returns>处置计划</returns>
    private static StreamRetryPlan GiveUpOr(bool exhausted, EStreamRetryAction action) =>
        new(exhausted ? EStreamRetryAction.GiveUp : action, TimeSpan.Zero);
}
