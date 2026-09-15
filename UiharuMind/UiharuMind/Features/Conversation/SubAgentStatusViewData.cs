/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>名下子代理此刻的处境。三档互斥，各占输入区上方一行</summary>
public enum ESubAgentStatusKind
{
    /// <summary>卡在嵌套审批上等人点选。<b>有时限</b>，到期按拒绝收口</summary>
    ApprovalWaiting,

    /// <summary>还在跑</summary>
    Running,

    /// <summary>结论已就绪，正等派活者空闲好交回</summary>
    HandoffQueued,
}

/// <summary>
/// 输入区上方那条<b>子代理状态</b>的一行。
///
/// 三档合成一个列表而不是三段各写各的 XAML：它们说的是同一件事的三种处境
/// （派出去了 → 跑着 / 卡在审批 / 跑完了压着等交回），显示规则也只有一条
/// ——「有就显示，点一下直达第一个」。从前只有「等审批」那一档有横幅，
/// 另外两档界面上一个字都没有，用户看到的是「派出去就没消息了」「回执丢了」。
/// </summary>
/// <param name="Kind">这一行说的是哪一档</param>
/// <param name="SubSessionIds">属于这一档的子会话（点一下打开第一个）</param>
public sealed record SubAgentStatusViewData(ESubAgentStatusKind Kind, IReadOnlyList<string> SubSessionIds)
{
    /// <summary>这一档有几个</summary>
    public int Count => SubSessionIds.Count;

    /// <summary>行文案</summary>
    public string Text => Loc.Text(Kind switch
    {
        ESubAgentStatusKind.ApprovalWaiting => "SubAgentApprovalWaiting",
        ESubAgentStatusKind.Running => "SubAgentRunningBanner",
        _ => "SubAgentHandoffQueuedBanner",
    });

    /// <summary>
    /// 是<b>报警</b>档（配色与其余两档分开）。等审批有时限、到期那次委派白跑，
    /// 而跑着与压着都只是「在等」，不该抢同样的注意力
    /// </summary>
    public bool IsAlert => Kind == ESubAgentStatusKind.ApprovalWaiting;

    /// <summary>状态圆点的配色键（<c>status-dot</c> 按 Tag 选色）</summary>
    public string DotTag => Kind switch
    {
        ESubAgentStatusKind.ApprovalWaiting => "Warning",
        ESubAgentStatusKind.Running => "Ready",
        _ => "Progress",
    };

    /// <summary>
    /// 这一档要不要转圈。只有「在跑」转：另外两档都停在那儿等人或等时机，
    /// 给它们转圈等于说「它正在推进」——那是假的
    /// </summary>
    public bool IsSpinning => Kind == ESubAgentStatusKind.Running;

    /// <summary>
    /// 按父会话现取三档。返回的顺序即显示顺序：要动手的排最前。
    /// </summary>
    /// <param name="parentSessionId">父会话标识</param>
    /// <returns>非空的那几档；都没有则空</returns>
    public static List<SubAgentStatusViewData> Collect(string? parentSessionId)
    {
        List<SubAgentStatusViewData> rows = [];
        Add(rows, ESubAgentStatusKind.ApprovalWaiting, BackgroundSubAgentDispatcher.ApprovalWaiting(parentSessionId));
        Add(rows, ESubAgentStatusKind.Running, BackgroundSubAgentDispatcher.RunningSubSessions(parentSessionId));
        Add(rows, ESubAgentStatusKind.HandoffQueued, BackgroundSubAgentDispatcher.HandoffQueued(parentSessionId));
        return rows;
    }

    private static void Add(List<SubAgentStatusViewData> rows, ESubAgentStatusKind kind, IReadOnlyList<string> ids)
    {
        if (ids.Count > 0) rows.Add(new SubAgentStatusViewData(kind, ids));
    }
}
