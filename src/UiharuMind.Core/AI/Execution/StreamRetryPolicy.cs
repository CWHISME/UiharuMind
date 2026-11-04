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

    /// <summary>退避后原样重发（上游安全检查临时拒绝，请求体没有可改的地方）</summary>
    RetryRejectedRequest,
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

    /// <summary>
    /// 上游安全检查拒绝（400 <c>safety check failed</c>）。这一码既可能是节点抖动，也可能是对内容的判决——
    /// 分不清，所以只做一次最小代价的尝试，见 <see cref="StreamRetryPolicy.MaxSafetyRetries"/>
    /// </summary>
    ProviderSafety,

    /// <summary>以上都不是：原样抛给上层</summary>
    Fatal,
}

/// <summary>这一发已经用掉的预算。各条重发路径各记各的，谁也吃不掉别人的额度</summary>
/// <param name="MaxTokensFixed">max_tokens 的配置已经改过一次</param>
/// <param name="ReasoningLearned">「每条助手消息都要带思考」已经记过一次</param>
/// <param name="DegenerateRetries">已经因退化重发过几次</param>
/// <param name="SafetyRetries">已经因上游安全检查重发过几次</param>
/// <param name="TransportRetries">已经因断流重发过几次（断线退避的指数也按它算，别掺进别的重发）</param>
internal readonly record struct StreamRetryBudget(bool MaxTokensFixed, bool ReasoningLearned,
    int DegenerateRetries, int SafetyRetries = 0, int TransportRetries = 0);

/// <summary>这一次的处置</summary>
/// <param name="Action">要做什么</param>
/// <param name="Delay">退避时长（<see cref="EStreamRetryAction.RetryAfterDelay"/> 与
/// <see cref="EStreamRetryAction.RetryRejectedRequest"/> 用得到）</param>
internal readonly record struct StreamRetryPlan(EStreamRetryAction Action, TimeSpan Delay);

/// <summary>
/// 一次流式失败该不该重发、怎么重发——<b>纯判定</b>：不碰配置、不发请求、不写日志。
///
/// <para>
/// 单独成一处，是因为这几条分支各自都带预算（自愈只做一次、退化两次、上游审查拒绝一次、断流五次），
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
    /// <summary>断流整段重发的尝试次数上限（含首发）。只有断流吃这个名额，其余重发路径各记各的</summary>
    public const int MaxAttempts = 6;

    /// <summary>断流重发的次数上限：首发 + 这几次 = <see cref="MaxAttempts"/> 次尝试</summary>
    public const int MaxTransportRetries = MaxAttempts - 1;

    /// <summary>
    /// 退化回复的重发次数上限。预算与断线分开记——它不是网络问题，等一等不会变好；
    /// 同一个模型连续几次都退化的概率不低，重发太多只是白烧 token
    /// </summary>
    public const int MaxDegenerateRetries = 2;

    /// <summary>
    /// 上游安全检查拒绝的原样重发次数上限。只给一次是刻意的保守：这一码既能是节点抖动，也能是
    /// 对内容的判决（ADR 0052 记的同一个码还盖着「引擎不可用」），而我们分不出是哪一种。
    /// 于是只花一次重发的代价去碰运气——真判决的话再试一次也不会过，只是再白付一次上下文。
    /// 等待时长也同理：实测里同一请求「几分钟后」能过，3 秒只盖得住秒级抖动，刻意不拉长；
    /// 命中就是白赚，命中率从 [stream] upstream safety check rejected 那两行日志的配对里数
    /// </summary>
    public const int MaxSafetyRetries = 1;

    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(1); //退避基数:1s、2s、4s…
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(8); //封顶:连续断线时等太久收益有限
    private static readonly TimeSpan RejectionDelay = TimeSpan.FromSeconds(3); //上游审查拒绝后等一拍,只盖秒级抖动（依据见 MaxSafetyRetries）

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
        if (SafetyCheckErrorHandler.IsSafetyCheckFailure(failure)) return EStreamFailureKind.ProviderSafety;
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

        // 原样重发也吃自己那份预算，不占断线的次数：一次抖动不该把「网线断五次」的额度提前用掉
        EStreamFailureKind.ProviderSafety => budget.SafetyRetries >= MaxSafetyRetries
            ? new StreamRetryPlan(EStreamRetryAction.GiveUp, TimeSpan.Zero)
            : new StreamRetryPlan(EStreamRetryAction.RetryRejectedRequest, RejectionDelay),

        EStreamFailureKind.TransportDrop => budget.TransportRetries >= MaxTransportRetries
            ? new StreamRetryPlan(EStreamRetryAction.GiveUp, TimeSpan.Zero)
            : new StreamRetryPlan(EStreamRetryAction.RetryAfterDelay, Delay(budget.TransportRetries + 1)),

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
