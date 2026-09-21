/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Conversation;

/// <summary>
/// 忙碌原因的文案映射：每一档都得有话说，而话在界面这一层（Core 侧不带文案，见 <see cref="ETurnBusy"/>）。
///
/// 这条会静默坏掉——往枚举里加一档，编译器不会提醒任何人去补文案，
/// 症状是提示块该出现时空着一行图标转圈、一个字没有。
/// </summary>
public class TurnBusyLabelTests
{
    /// <summary>
    /// 与 <c>ConversationViewModel.BusyLabel</c> 逐字同形的映射表。
    ///
    /// 复制一份而不是去实例化那个视图模型：后者的构造会碰账本、转录器与几个全局单例，
    /// 而这里要测的只是这张表本身有没有缺项。
    /// </summary>
    private static readonly Dictionary<ETurnBusy, string> Keys = new()
    {
        [ETurnBusy.ConnectingMcp] = "AgentMcpConnecting",
        [ETurnBusy.Compacting] = "HandoffWriting",
    };

    /// <summary>加了新档位就必须同时补上映射，否则这里当场失败</summary>
    [Fact]
    public void EveryBusyReason_IsMapped()
    {
        IEnumerable<ETurnBusy> needsCopy = Enum.GetValues<ETurnBusy>().Where(x => x != ETurnBusy.None);

        Assert.Equal(needsCopy.Order(), Keys.Keys.Order());
    }

    /// <summary>
    /// 每个键在两种语言下都要真的取到译文。
    ///
    /// 断言「不等于键名」而不是「非空」：<c>GetString</c> 缺键时回退成键名本身，
    /// 只判空的话这条测试对漏译一无所知。
    /// </summary>
    [Theory]
    [InlineData("zh-Hans")]
    [InlineData("en")]
    public void EveryBusyReason_HasCopy(string culture)
    {
        LocalizationManager.Instance.ApplyLanguage(culture, save: false);

        foreach ((ETurnBusy busy, string key) in Keys)
        {
            string label = LocalizationManager.Instance.GetString(key);
            Assert.NotEqual(key, label); //回退成键名 = 这个语言里没有这条
            Assert.False(string.IsNullOrWhiteSpace(label), $"{culture} 的 {busy} 文案是空的");
        }
    }
}
