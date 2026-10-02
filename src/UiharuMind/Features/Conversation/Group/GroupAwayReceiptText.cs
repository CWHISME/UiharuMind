using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 离席回执画成群里的一段旁白（ADR 0055）：为什么停、历时多久、化身替你说了几句、点了哪些审批、自己做了什么、它的交代。
/// 驳回的审批逐条列——那是用户回来最可能要补做的事
/// </summary>
public static class GroupAwayReceiptText
{
    /// <summary>
    /// 回执的显示文本
    /// </summary>
    /// <param name="receipt">回执</param>
    /// <returns>多行文本</returns>
    public static string Format(GroupAwayReceipt receipt)
    {
        List<string> lines =
        [
            Loc.Text(LangKey.GroupAwayReceiptTitleFormat, ReasonText(receipt.Reason)),
            Loc.Text(LangKey.GroupAwayReceiptStatsFormat, DurationText(receipt.EndedAt - receipt.StartedAt),
                receipt.AvatarTurns, receipt.AvatarPostIndices.Count),
        ];

        if (receipt.Approvals.Count > 0)
        {
            int approved = receipt.Approvals.Count(x => x.Approved);
            lines.Add(Loc.Text(LangKey.GroupAwayReceiptApprovalsFormat, receipt.Approvals.Count, approved,
                receipt.Approvals.Count - approved));
            lines.AddRange(receipt.Approvals.Where(x => !x.Approved)
                .Select(x => Loc.Text(LangKey.GroupAwayReceiptDeniedFormat, x.MemberName, x.ToolName, x.Reason)));
        }

        if (receipt.AvatarActions.Count > 0)
            lines.Add(Loc.Text(LangKey.GroupAwayReceiptActionsFormat, string.Join("；", receipt.AvatarActions)));
        if (!string.IsNullOrWhiteSpace(receipt.Summary))
            lines.Add(Loc.Text(LangKey.GroupAwayReceiptSummaryFormat, receipt.Summary.Trim()));

        return string.Join("\n", lines);
    }

    private static string ReasonText(EGroupAwayEndReason reason) => Loc.Text(reason switch
    {
        EGroupAwayEndReason.Done => LangKey.GroupAwayReasonDone,
        EGroupAwayEndReason.NeedsUser => LangKey.GroupAwayReasonNeedsUser,
        EGroupAwayEndReason.Manual => LangKey.GroupAwayReasonManual,
        EGroupAwayEndReason.DurationFuse => LangKey.GroupAwayReasonDurationFuse,
        EGroupAwayEndReason.TurnsFuse => LangKey.GroupAwayReasonTurnsFuse,
        EGroupAwayEndReason.IdleFuse => LangKey.GroupAwayReasonIdleFuse,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, null),
    });

    private static string DurationText(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1
            ? Loc.Text(LangKey.GroupAwayDurationHoursFormat, (int)duration.TotalHours, duration.Minutes)
            : Loc.Text(LangKey.GroupAwayDurationMinutesFormat, Math.Max(1, (int)Math.Round(duration.TotalMinutes)));
    }
}
