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
using Avalonia.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Conversation;

public partial class AgentPage : UserControl
{
    public AgentPage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is AgentPageData data) data.UpdateResponsiveState(e.NewSize.Width);
    }

    /// <summary>
    /// 展开前给角色选择器换一份数据：角色可能刚被新建/删除/改名，
    /// 而当前角色应当从候选里排除（选中自己是空操作）。
    /// agent 页只列 agent 档角色——工具、权限档、文件记忆这些装配只对 agent 档生效，
    /// 在这里选中一个角色扮演角色，得到的会是个没工具却带扮演脚手架的东西。
    /// </summary>
    private void OnCharacterPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not AgentPageData data) return;

        ConversationViewModel conversation = data.Conversation;
        Flyout? flyout = sender as Flyout; //Opening 的 sender 就是 Flyout 本身,拿它收起面板
        CharacterPicker.DataContext = new CharacterPickerViewData(
            character =>
            {
                conversation.ChangeCharacter(character);
                flyout?.Hide();
            },
            filter: character => character.Kind.IsAgent(),
            excludedIds: [conversation.ActiveCharacterId]);
    }

    /// <summary>
    /// 展开前重建最近工作区列表：这份列表也会被设置页(默认工作目录)写入，
    /// 而那条路径不经过会话，光靠 WorkspacePath 变化刷新会漏掉。
    /// </summary>
    private void OnRecentWorkspacesOpening(object? sender, EventArgs e)
    {
        if (DataContext is AgentPageData data) data.Conversation.RefreshRecentWorkspaces();
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.LeftPaneWidth = Math.Clamp(data.LeftPaneWidth + (float)e.Vector.X, 120, 400);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.RightPaneWidth = Math.Clamp(data.RightPaneWidth - (float)e.Vector.X, 120, 400);
    }
}