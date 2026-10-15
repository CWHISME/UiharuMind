using System;
using System.Linq;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Features.Conversation;
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
        Assert.Equal(expected, WakeLoopViewData.CountdownText(TimeSpan.FromSeconds(seconds)));
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
    public void Receipt_MultipleActions_OnePerLine()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        GroupAwayReceipt receipt = new("g", "avatar", EGroupAwayEndReason.Done, "",
            start, start.AddMinutes(10), 2, [], [],
            ["Edit src/a.cs", "Read src/b.cs", "Grep docs"]);

        string[] lines = GroupAwayReceiptText.Format(receipt).Split('\n');

        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptActionsFormat, "Edit src/a.cs"), lines[2]);
        Assert.Equal("Read src/b.cs", lines[3]);
        Assert.Equal("Grep docs", lines[4]);
    }

    [Fact]
    public void Receipt_TooManyActions_CapsTheListAndCountsTheRest()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        string[] actions = Enumerable.Range(0, 15).Select(i => $"Read src/f{i}.cs").ToArray();
        GroupAwayReceipt receipt = new("g", "avatar", EGroupAwayEndReason.Done, "",
            start, start.AddMinutes(10), 2, [], [], actions);

        string[] lines = GroupAwayReceiptText.Format(receipt).Split('\n');

        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptActionsFormat, "Read src/f0.cs"), lines[2]);
        for (int i = 1; i < 12; i++)
            Assert.Equal($"Read src/f{i}.cs", lines[2 + i]);
        Assert.Equal(Loc.Text(LangKey.GroupAwayReceiptActionsMoreFormat, 3), lines[14]);
    }

    [Fact]
    public void ReceiptBrief_IsTwoLines_WithoutApprovalsActionsOrSummary()
    {
        DateTimeOffset start = DateTimeOffset.UnixEpoch;
        GroupAwayReceipt receipt = new("g", "avatar", EGroupAwayEndReason.NeedsUser, "要不要推送等你定",
            start, start.AddMinutes(95), 6, [3, 9],
            [new GroupAwayApproval("Bob", "Write /etc/hosts", false, "越界写入")],
            ["Edit src/a.cs"]);

        string[] lines = GroupAwayReceiptText.FormatBrief(receipt).Split('\n');

        Assert.Equal(2, lines.Length);
        Assert.Equal(GroupAwayReceiptText.Format(receipt).Split('\n')[..2], lines);
        Assert.DoesNotContain("越界写入", GroupAwayReceiptText.FormatBrief(receipt));
        Assert.DoesNotContain("Edit src/a.cs", GroupAwayReceiptText.FormatBrief(receipt));
        Assert.DoesNotContain("要不要推送等你定", GroupAwayReceiptText.FormatBrief(receipt));
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
