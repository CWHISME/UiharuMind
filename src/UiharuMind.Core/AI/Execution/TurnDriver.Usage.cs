using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Usage;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 用量埋点。与 <see cref="TurnDriver"/> 同属一个类，拆在这个文件里是因为它是一整套
/// 「一次服务调用一行」的口径，混在轮次编排里看不清边界。
///
/// <para>
/// 三件事只在这里定：<b>模型身份怎么取</b>（<see cref="LazyChatClient.ServingModel"/> 判据）、
/// <b>一次调用什么时候算结束</b>（成功收口 / 失败出口）、<b>失败前收到的量怎么办</b>（并进失败行）。
/// 计价由 <see cref="UsageModelIdentity"/> 负责，这里不碰价格表。
/// </para>
/// </summary>
public sealed partial class TurnDriver
{
    /// <summary>本轮<b>在途</b>的那一次服务调用。终态写完后仍留着（<see cref="UsageCallTracker.IsSettled"/> 为真），
    /// 但不会重复写——外层 catch 靠它把「已收到量之后才断流」的那笔并进同一行。
    /// 一轮之内可以发生多次调用（框架自动跑工具往返），每次调用各写一行，见 <see cref="UsageCallTracker"/></summary>
    private UsageCallTracker? _activeUsageCall;

    /// <summary>
    /// 取「本轮真正发请求的模型」的身份快照。<b>不能用 <c>session.ChatModelRunningData</c> 直接当判据</b>：
    /// 全局跑着 B、会话钉着一个没在跑的本地 A 时，实际请求发给 B，按 A 记就是张冠李戴
    /// </summary>
    private static UsageModelIdentity ResolveUsageIdentity(ChatSession session) =>
        UsageModelIdentity.From(
            LazyChatClient.ServingModel(session.ChatModelRunningData, LlmManager.Instance.EffectiveGlobalModel),
            DateTimeOffset.Now);

    /// <summary>
    /// 一次服务调用开始：把发出时刻与模型身份定格，返回的记账器在终态写一行
    /// </summary>
    private UsageCallTracker BeginUsageCall(ChatSession session) =>
        _activeUsageCall = UsageCallTracker.Begin(session.SessionId, ResolveUsageIdentity(session));

    /// <summary>
    /// 失败出口。<b>只收在途的那一次调用</b>——它自己的行会带上已收到的量。
    ///
    /// <para>
    /// 没有在途调用（已结清）时<b>不补行</b>：那种失败发生在两次调用之间（审批等待、落盘、装配），
    /// 根本没有一次请求发生。从前补一行 0 token 的失败，等于往「请求数」和错误率的分母里
    /// 混进没发生过的请求——与右侧面板按请求计数的口径对不上。这类失败看日志与界面提示就够。
    /// </para>
    /// </summary>
    private void FailUsageCall(string errorKind, Exception? error, int? statusCode = null)
    {
        if (_activeUsageCall is { IsSettled: false } call) call.Fail(errorKind, error, statusCode);
    }
}
