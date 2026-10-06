using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>离席为什么结束（ADR 0055）</summary>
public enum EGroupAwayEndReason
{
    /// <summary>化身判定目标达成</summary>
    Done,

    /// <summary>化身碰到必须用户本人定的事</summary>
    NeedsUser,

    /// <summary>用户手动结束</summary>
    Manual,

    /// <summary>保险丝：离席时长到顶</summary>
    DurationFuse,

    /// <summary>保险丝：化身出手次数到顶</summary>
    TurnsFuse,

    /// <summary>保险丝：连续好几波没有新产物</summary>
    IdleFuse,
}

/// <summary>
/// 一次离席的参数，开离席时从设置定格
/// </summary>
/// <param name="MaxDuration">离席最长多久</param>
/// <param name="MaxAvatarTurns">化身最多出手几次</param>
/// <param name="MaxIdleWaves">连续多少波没有新产物就结束</param>
/// <param name="BackoffStart">没进展时第一次延迟唤醒等多久</param>
/// <param name="BackoffMax">没进展时延迟唤醒的封顶</param>
/// <param name="StopDelay">用户按了停止后等多久再唤醒</param>
public sealed record GroupAwaySettings(TimeSpan MaxDuration, int MaxAvatarTurns, int MaxIdleWaves,
    TimeSpan BackoffStart, TimeSpan BackoffMax, TimeSpan StopDelay)
{
    /// <summary>
    /// 从全局设置取（下限兜住，填 0 或负数不至于让离席原地收场或一直空转）
    /// </summary>
    /// <param name="config">智能体设置</param>
    /// <returns>离席参数</returns>
    public static GroupAwaySettings From(AgentSettingConfig config) => new(
        TimeSpan.FromHours(Math.Max(1, config.AwayMaxHours)),
        Math.Max(1, config.AwayMaxAvatarTurns),
        Math.Max(1, config.AwayMaxIdleWaves),
        TimeSpan.FromSeconds(Math.Max(5, config.AwayBackoffStartSeconds)),
        TimeSpan.FromMinutes(Math.Max(1, config.AwayBackoffMaxMinutes)),
        TimeSpan.FromMinutes(Math.Max(1, config.AwayStopDelayMinutes)));

    /// <summary>
    /// 第 n 次没进展（从 0 起）该等多久：逐次翻倍、封顶
    /// </summary>
    /// <param name="level">连续没进展的次数</param>
    /// <returns>延迟</returns>
    public TimeSpan BackoffOf(int level)
    {
        double factor = Math.Pow(2, Math.Min(level, 30));
        return TimeSpan.FromTicks((long)Math.Min(BackoffMax.Ticks, BackoffStart.Ticks * factor));
    }
}

/// <summary>
/// 离席此刻的样子（界面用）
/// </summary>
/// <param name="GroupId">群壳会话标识</param>
/// <param name="AvatarSessionId">化身会话标识</param>
/// <param name="StartedAt">开始时刻</param>
/// <param name="AvatarTurns">化身已出手几次</param>
/// <param name="IsAvatarRunning">化身正在跑</param>
/// <param name="WakeAt">延迟唤醒的时刻；没在等为 null</param>
/// <param name="Goal">只给化身看的捎话；没填为空</param>
/// <param name="Reminder">只给化身看的重要提醒；没有为 null</param>
/// <param name="IsInfinite">无限模式</param>
public sealed record GroupAwayStatus(string GroupId, string AvatarSessionId, DateTimeOffset StartedAt, int AvatarTurns,
    bool IsAvatarRunning, DateTimeOffset? WakeAt, string Goal, string? Reminder, bool IsInfinite);

/// <summary>
/// 离席期间化身点过的一条审批
/// </summary>
/// <param name="MemberName">请求审批的是谁（成员名，或化身自己）</param>
/// <param name="ToolName">工具名</param>
/// <param name="Approved">批了为 true</param>
/// <param name="Reason">为什么这么点（越界写入、判定失败也写在这里）</param>
public sealed record GroupAwayApproval(string MemberName, string ToolName, bool Approved, string Reason);

/// <summary>
/// 离席回执的原料：离席结束时交出，界面画成一张只给人看的卡
/// </summary>
/// <param name="GroupId">群壳会话标识</param>
/// <param name="AvatarSessionId">化身会话标识</param>
/// <param name="Reason">为什么结束</param>
/// <param name="Summary">化身给用户的交代；保险丝与手动结束为空</param>
/// <param name="StartedAt">开始时刻</param>
/// <param name="EndedAt">结束时刻</param>
/// <param name="AvatarTurns">化身出手几次</param>
/// <param name="AvatarPostIndices">化身替用户说的群发言在群流水里的下标</param>
/// <param name="Approvals">化身点过的审批</param>
/// <param name="AvatarActions">化身自己动手做的事（工具名 + 路径或命令），按先后</param>
public sealed record GroupAwayReceipt(string GroupId, string AvatarSessionId, EGroupAwayEndReason Reason,
    string Summary, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int AvatarTurns,
    IReadOnlyList<int> AvatarPostIndices, IReadOnlyList<GroupAwayApproval> Approvals,
    IReadOnlyList<string> AvatarActions)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { Converters = { new JsonStringEnumConverter() } };

    /// <summary>序列化成 JSON（随群流水那条回执落盘）</summary>
    /// <returns>JSON</returns>
    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>
    /// 从群流水里的一条读回执
    /// </summary>
    /// <param name="message">群流水里的一条</param>
    /// <returns>回执；不是回执或读不出为 null</returns>
    public static GroupAwayReceipt? Of(ChatMessage message) =>
        ChatMessageAnnotations.GroupAwayReceiptOf(message) is { } json ? FromJson(json) : null;

    /// <summary>
    /// 从 JSON 读回
    /// </summary>
    /// <param name="json">JSON</param>
    /// <returns>回执；读不出为 null</returns>
    public static GroupAwayReceipt? FromJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<GroupAwayReceipt>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
