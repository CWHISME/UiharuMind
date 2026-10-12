namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>波末该拿化身怎么办</summary>
public enum EGroupAwayWake
{
    /// <summary>空闲保险丝：结束离席</summary>
    FinishIdle,

    /// <summary>用户停了这一波：固定延迟后再叫</summary>
    WaitStop,

    /// <summary>成员还有后台工作没交回：这次不叫，等他交回的那一波收场时再叫</summary>
    DeferBackground,

    /// <summary>化身自己那句没人接：退避后再叫</summary>
    BackoffEmpty,

    /// <summary>立刻再叫化身来看一眼</summary>
    WakeNow,
}

/// <summary>
/// 波末唤醒决策（与 <see cref="GroupWakePolicy"/> 同一路：纯函数，好单测）。
/// 优先级：保险丝 ＞ 用户停止 ＞ 等后台 ＞ 空推退避 ＞ 立刻唤醒。
/// 用户停止优先于等后台：用户亲手停的，化身要按停止口径回话，而不是悄悄等；
/// 等后台优先于空推：化身自己那句没人接时，若成员后台还没跑完，现在叫也是错序
/// </summary>
public static class GroupAwayWakePolicy
{
    /// <summary>
    /// 判一下波末该拿化身怎么办
    /// </summary>
    /// <param name="idleFuse">空闲保险丝熔断</param>
    /// <param name="stopped">这一波是被用户停下的</param>
    /// <param name="emptyPush">化身自己那句开的波、成员一句没说</param>
    /// <param name="hasBackgroundWork">成员还有后台工作没交回（在跑的任务/子代理，或等着叫他开口的附注）</param>
    /// <returns>下一步</returns>
    public static EGroupAwayWake Decide(bool idleFuse, bool stopped, bool emptyPush, bool hasBackgroundWork) =>
        idleFuse ? EGroupAwayWake.FinishIdle
        : stopped ? EGroupAwayWake.WaitStop
        : hasBackgroundWork ? EGroupAwayWake.DeferBackground
        : emptyPush ? EGroupAwayWake.BackoffEmpty
        : EGroupAwayWake.WakeNow;
}
