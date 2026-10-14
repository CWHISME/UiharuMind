/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using UiharuMind.Features.Conversation;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 运行耗时排版：语言无关，只放数字。
/// </summary>
public class RunElapsedFormatTests
{
    [Fact]
    public void UnderAMinute_ShowsSeconds()
    {
        Assert.Equal("0s", ConversationViewModel.FormatRunElapsed(TimeSpan.Zero));
        Assert.Equal("7s", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(7.9)));
        Assert.Equal("59s", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(59)));
    }

    [Fact]
    public void UnderAnHour_ShowsMinuteSecond()
    {
        Assert.Equal("01:00", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(60)));
        Assert.Equal("01:23", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(83)));
        Assert.Equal("59:59", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(3599)));
    }

    [Fact]
    public void OverAnHour_ShowsHourMinuteSecond()
    {
        Assert.Equal("1:02:03", ConversationViewModel.FormatRunElapsed(new TimeSpan(1, 2, 3)));
    }

    [Fact]
    public void Negative_ClampsToZero()
    {
        Assert.Equal("0s", ConversationViewModel.FormatRunElapsed(TimeSpan.FromSeconds(-5)));
    }
}
