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
        (((ChatPageData)DataContext!)).PaneWidth += (float)e.Vector.X;
    }
    
    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        (((ChatPageData)DataContext!)).RightPaneWidth -= (float)e.Vector.X;
    }
}