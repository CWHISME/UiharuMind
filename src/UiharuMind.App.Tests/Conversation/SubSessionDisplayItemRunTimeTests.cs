/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 右栏「子代理」面板的运行时间列与排序。
///
/// 背景：一个子会话可以被续跑多次（ContinueAsync 复用同一会话），所以「运行时间」指
/// <b>最近一轮</b>——运行中「本次已运行」、跑完「末轮耗时」；旧数据（无 LastRunStartedAt）
/// 回退最后更新时间戳，不硬算墙钟跨度。
/// </summary>
public class SubSessionDisplayItemRunTimeTests
{
    [Fact]
    public void RunTimeText_RunningShowsStopwatchSinceLastRunStarted()
    {
        ChatSessionMeta meta = new()
        {
            SessionId = "run-1",
            BackgroundReportPending = true,
            LastRunStartedAt = DateTimeOffset.Now.AddSeconds(-65),
        };

        SubSessionDisplayItem item = new(meta);

        Assert.True(item.IsRunning);
        // 65 秒前开始 → 01:0x；只断言格式，不赌执行快慢的秒位
        Assert.Matches(new Regex(@"^本次 01:0\d$"), item.RunTimeText);
    }

    [Fact]
    public void RunTimeText_FinishedShowsLastRoundDuration()
    {
        DateTimeOffset started = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);
        ChatSessionMeta meta = new()
        {
            SessionId = "done-1",
            LastRunStartedAt = started,
            UpdatedAt = started.AddMinutes(8).AddSeconds(42),
        };

        SubSessionDisplayItem item = new(meta);

        Assert.False(item.IsRunning);
        Assert.Equal("末轮 08:42", item.RunTimeText);
    }

    [Fact]
    public void RunTimeText_LegacyDataWithoutRunStartFallsBackToTimestamp()
    {
        // 用本地时区偏移构造，断言才与时区无关（ToLocalTime 对同偏移无操作）
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 5, 6));
        DateTimeOffset updated = new(2024, 5, 6, 7, 8, 0, offset);
        ChatSessionMeta meta = new()
        {
            SessionId = "legacy-1",
            UpdatedAt = updated,
        };

        SubSessionDisplayItem item = new(meta);

        Assert.Equal("05/06 07:08", item.RunTimeText);
    }

    [Fact]
    public void RunTimeTip_RunningSaysInProgress_FinishedShowsBothEnds()
    {
        TimeSpan offset = TimeZoneInfo.Local.GetUtcOffset(new DateTime(2024, 1, 2));
        DateTimeOffset started = new(2024, 1, 2, 3, 4, 5, offset);
        SubSessionDisplayItem running = new(new ChatSessionMeta
        {
            SessionId = "tip-run",
            BackgroundReportPending = true,
            LastRunStartedAt = started,
        });
        SubSessionDisplayItem done = new(new ChatSessionMeta
        {
            SessionId = "tip-done",
            LastRunStartedAt = started,
            UpdatedAt = started.AddMinutes(3),
        });

        Assert.Equal("开始 01/02 03:04:05 · 进行中", running.RunTimeTip);
        Assert.Equal("开始 01/02 03:04:05 · 结束 01/02 03:07:05", done.RunTimeTip);
    }

    [Fact]
    public void IsLongRunning_UsesCurrentRoundStart_NotCreationTime()
    {
        // 会话创建很早（续跑过的），但本轮刚开：不该被标成「跑太久」
        SubSessionDisplayItem item = new(new ChatSessionMeta
        {
            SessionId = "fresh-round",
            BackgroundReportPending = true,
            CreatedAt = DateTimeOffset.Now.AddHours(-5),
            LastRunStartedAt = DateTimeOffset.Now.AddSeconds(-10),
        });

        Assert.False(item.IsLongRunning);
    }

    [Fact]
    public void OrderItems_PutsRunningFirst_ThenByStartDesc_ThenDoneByUpdatedDesc()
    {
        DateTimeOffset t0 = new(2024, 1, 2, 3, 0, 0, TimeSpan.Zero);

        ChatSessionMeta olderRunning = new() { SessionId = "r-old", BackgroundReportPending = true, LastRunStartedAt = t0 };
        ChatSessionMeta newerRunning = new() { SessionId = "r-new", BackgroundReportPending = true, LastRunStartedAt = t0.AddMinutes(2) };
        ChatSessionMeta olderDone = new() { SessionId = "d-old", UpdatedAt = t0.AddMinutes(10) };
        ChatSessionMeta newerDone = new() { SessionId = "d-new", UpdatedAt = t0.AddMinutes(12) };

        // 故意乱序传入：排序应把运行中置顶、组内按本轮开始倒序、已完成按结束时刻倒序
        var ordered = SubAgentListViewData.OrderItems([olderDone, newerRunning, olderRunning, newerDone]);

        Assert.Equal(["r-new", "r-old", "d-new", "d-old"], ordered.ConvertAll(x => x.SessionId));
    }
}
