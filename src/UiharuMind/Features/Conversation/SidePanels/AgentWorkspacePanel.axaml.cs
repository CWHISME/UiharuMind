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
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation.Pages;

namespace UiharuMind.Features.Conversation.SidePanels;

/// <summary>
/// 智能体右栏的工作区卡片：运行态、换角色、工作目录的展示与切换。
/// DataContext 沿逻辑树继承，即 <see cref="ConversationPageData"/>。
/// </summary>
public partial class AgentWorkspacePanel : UserControl
{
    public AgentWorkspacePanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 展开前给角色选择器换一份数据：角色可能刚被新建/删除/改名，
    /// 而当前角色应当从候选里排除（选中自己是空操作）。
    /// 候选按页面当前类型过滤：agent 档的工具、权限档、文件记忆装配只对 agent 档生效，
    /// 在这里选中一个角色扮演角色，得到的会是个没工具却带扮演脚手架的东西——
    /// 普通对话同理反过来。两类统一懒建后这张卡两边都在用，过滤必须跟类型走。
    /// </summary>
    private void OnCharacterPickerOpening(object? sender, EventArgs e)
    {
        if (DataContext is not ConversationPageData data) return;

        ConversationViewModel conversation = data.Conversation;
        Flyout? flyout = sender as Flyout; //Opening 的 sender 就是 Flyout 本身,拿它收起面板
        CharacterPicker.DataContext = new CharacterPickerViewData(
            character =>
            {
                // 空态即预选（只改新建默认值，发送才建会话）；预选名字显示在角色行的头像旁
                conversation.ChangeCharacter(character);
                flyout?.Hide();
            },
            filter: character => data.CurrentType == EConversationType.Chat
                ? character.Kind.IsChat()
                : character.Kind.IsAgent(),
            excludedIds: [conversation.ActiveCharacterId]);
    }

    /// <summary>
    /// 目录行展开前重建最近工作区列表：这份列表也会被设置页(默认工作目录)写入，
    /// 而那条路径不经过会话，光靠 WorkspacePath 变化刷新会漏掉。
    /// </summary>
    private void OnWorkspaceFlyoutOpening(object? sender, EventArgs e)
    {
        if (DataContext is ConversationPageData data) data.Conversation.Workspace.RefreshRecent();
    }
}
