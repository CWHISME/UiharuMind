namespace UiharuMind.Core.AI.Chat.WakeLoops;

/// <summary>
/// 延迟唤醒到点后「这次能不能叫」的结论。
///
/// <b>只回答延迟到点这一件事</b>：首轮与手动唤醒（<c>GoalModeController.Wake</c> → <c>TryClaimTurn</c>）
/// 不走它，那两条是「这一刻就该跑」的诉求，忙也要记成欠着。占轮阶段另有一套判据（含本循环自己
/// 是否已收尾，那个维度 <see cref="WakeLoopSession{TReason}.ClaimDue"/> 不管），只是复用同一个枚举。
/// 到点这一半的判据只由 <see cref="WakeLoopSession{TReason}.ClaimDue"/> 给，别在控制器里另拼
/// </summary>
internal enum EWakeDue
{
    /// <summary>不用叫：那次等待已被重新排程，或循环已经结束</summary>
    Cancelled,

    /// <summary>
    /// 叫不了：会话此刻接不了一轮（用户在跑自己那一轮）。闸只管「这一次不算发生」——
    /// 不注入、不计轮、不动 IsRunning；补不补由各域自己的口径定
    /// （单聊：墙钟锚定的规则欠一次，其余按新的停下重算；群离席：波末重判）
    /// </summary>
    Busy,

    /// <summary>认领到了，去唤醒</summary>
    Go,
}
