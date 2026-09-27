namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员这一轮是被谁叫醒的。保守档的一跳防护按它判断（ADR 0049 决策 6）。
/// 数值越小越「强」：同时有多个来由时取最小的
/// </summary>
public enum EGroupWakeCause
{
    /// <summary>用户发言、或用户点了「继续」</summary>
    User,

    /// <summary>主持人点名</summary>
    Host,

    /// <summary>别的成员发言（@ 或小群必答）</summary>
    Member,
}
