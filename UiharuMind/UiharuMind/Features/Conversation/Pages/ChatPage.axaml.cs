/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using Avalonia.Controls;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>骨架与交互都在 ConversationPageShell,本页只提供三处内容槽与新建会话的角色选择</summary>
public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 新建会话前选角色。<b>只列扮演与工具人两档</b>：智能体属于智能体页——
    /// 它要工作目录、权限档与右侧栏那套面板，在聊天页开出来是个缺零件的东西。
    /// </summary>
    private void OnNewChatCharacterPickerOpening(object? sender, EventArgs e)
    {
        Flyout? flyout = sender as Flyout;
        NewChatCharacterPicker.DataContext = new CharacterPickerViewData(
            character =>
            {
                flyout?.Hide();
                SessionManager.Instance.StartNewSession(character);
            },
            filter: character => character.Kind.IsChat());
    }
}
