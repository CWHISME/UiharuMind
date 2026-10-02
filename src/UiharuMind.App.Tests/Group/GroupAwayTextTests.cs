using System;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Features.Conversation.Group;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using Xunit;

namespace UiharuMind.App.Tests.Group;

/// <summary>离席（ADR 0055）的两处显示文本：回执旁白与延迟唤醒的倒计时</summary>
public class GroupAwayTextTests
{
    [Theory]
    [InlineData(272, "4:32")]
    [InlineData(5, "0:05")]
    [InlineData(3725, "1:02:05")]
    [InlineData(-3, "0:00")]
    public void Countdown_IsMinutesSeconds_UnderAnHour(int seconds, string expected)
    {
        Assert.Equal(expected, GroupAwayViewData.CountdownText(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Receipt_ListsWhyHowLong_WhatWasDenied_AndTheNote()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        GroupAwayReceipt receipt = new("g", "avatar", EGroupAwayEndReason.NeedsUser, "要不要推送等你定",
            start, start.AddMinutes(95), 6, [3, 9],
            [new GroupAwayApproval("Alice", "Shell", true, "在推进"), new GroupAwayApproval("Bob", "Write /etc/hosts", false, "越界写入")],
            ["Edit src/a.cs"]);

        string[] lines = GroupAwayReceiptText.Format(receipt).Split('\n');

        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptTitleFormat, Loc.Text(LangKey.GroupAwayReasonNeedsUser)), lines[0]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptStatsFormat, Loc.Text(LangKey.GroupAwayDurationHoursFormat, 1, 35), 6, 2),
            lines[1]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptApprovalsFormat, 2, 1, 1), lines[2]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptDeniedFormat, "Bob", "Write /etc/hosts", "越界写入"), lines[3]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptActionsFormat, "Edit src/a.cs"), lines[4]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptSummaryFormat, "要不要推送等你定"), lines[5]);
    }

    [Fact]
    public void Receipt_WithNothingToReport_IsTwoLines()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        GroupAwayReceipt receipt = new("g", "avatar", EGroupAwayEndReason.Manual, "", start, start.AddSeconds(20), 0,
            [], [], []);

        Assert.Equal(2, GroupAwayReceiptText.Format(receipt).Split('\n').Length);
    }
}
