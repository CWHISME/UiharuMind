using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Usage;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 用量埋点。与 <see cref="TurnDriver"/> 同属一个类，拆在这个文件里是因为它是一整套
/// 「一次服务调用终态写一行」的口径，混在轮次编排里看不清边界。
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
    /// 但不会重复写——外层 catch 靠它把「已收到量之后才断流」的那笔并进同一行</summary>
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
    /// 失败收口。<b>有在途调用</b>就把它记成失败（已收到的量并进去，绝不另写一行）；
    /// <b>没有在途调用</b>才是「整轮级」失败，补一行 0 token 的失败让错误率看得见
    /// </summary>
    private void FailUsageCall(ChatSession session, string errorKind, Exception? error, int? statusCode = null)
    {
        if (_activeUsageCall is { IsSettled: false } call)
        {
            call.Fail(errorKind, error, statusCode);
            return;
        }

        UsageCallTracker.RecordTurnFailure(session.SessionId, ResolveUsageIdentity(session), errorKind, error);
    }
}
