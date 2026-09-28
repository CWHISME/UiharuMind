namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员这一轮是被谁叫醒的。保守档的一跳防护按它判断（ADR 0049 决策 6）。
/// 数值越小越「强」。两处取法不同：说完后补叫时，攒下的几条来由取最小的（最强）；
/// 跑着时又被点到，则按最近这一跳算（被成员点到降为第二跳，被主持人点名升为主持人叫醒），见 <see cref="ParallelGroupScheduler"/>
/// </summary>
public enum EGroupWakeCause
{
    /// <summary>用户发言、或用户点了「继续」</summary>
    User,

    /// <summary>主持人点名</summary>
    Host,

    /// <summary>别的成员发言（@ 或小群必答）</summary>
    Member,

    /// <summary>激进档的补位轮：一波静下来后，还有没看过的发言的人各补一次（ADR 0049 修订）</summary>
    CatchUp,
}
