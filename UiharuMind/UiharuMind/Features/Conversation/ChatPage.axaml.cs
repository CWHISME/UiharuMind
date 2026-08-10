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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Conversation;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is ChatPageData data) data.UpdateResponsiveState(e.NewSize.Width);
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

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (ChatPageData)DataContext!;
        data.LeftPaneWidth = Math.Clamp(data.LeftPaneWidth + (float)e.Vector.X, 230, 340);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (ChatPageData)DataContext!;
        data.RightPaneWidth = Math.Clamp(data.RightPaneWidth - (float)e.Vector.X, 280, 380);
    }
}
