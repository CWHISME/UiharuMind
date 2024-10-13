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
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.Pages;

public partial class ChatPage : UserControl
{
    public ChatPage()
    {
        InitializeComponent();
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (ChatPageData)DataContext!;
        data.LeftPaneWidth = Math.Clamp(data.LeftPaneWidth + (float)e.Vector.X, 0, 300);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (ChatPageData)DataContext!;
        data.RightPaneWidth = Math.Clamp(data.RightPaneWidth - (float)e.Vector.X, 0, 300);
    }
}