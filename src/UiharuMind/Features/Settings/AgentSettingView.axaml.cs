/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Controls;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Settings;

public partial class AgentSettingView : UserControl
{
    public AgentSettingView()
    {
        InitializeComponent();
        DataContext = new AgentSettingViewData();
    }

    /// <summary>
    /// 展开前换一份选择器数据：角色可能刚被新建、删除或改名。
    /// 顺带让 ViewData 复核一遍当前选中还在不在——设置页是缓存复用的，不查就会一直顶着一张旧卡。
    ///
    /// 候选闸门由 <see cref="CharacterPickerViewData"/> 自己走（内部卡不列、屏蔽卡按解锁开关），
    /// 这里只叠一条「用户卡不当子代理人格」——那是「我是谁」的单例，有自己的编辑窗。
    /// </summary>
    private void OnDefaultSubAgentCharacterPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not AgentSettingViewData data) return;

        Flyout? flyout = sender as Flyout; //Opening 的 sender 就是 Flyout 本身，选中后拿它收起面板
        data.RevalidateDefaultSubAgentCharacter();
        DefaultSubAgentCharacterPicker.DataContext = new CharacterPickerViewData(
            character =>
            {
                data.PickDefaultSubAgentCharacter(character);
                flyout?.Hide();
            },
            filter: static character => !character.IsUserCard);
    }
}
