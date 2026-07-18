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
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.Pages;

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

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.LeftPaneWidth = Math.Clamp(data.LeftPaneWidth + (float)e.Vector.X, 230, 340);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (AgentPageData)DataContext!;
        data.RightPaneWidth = Math.Clamp(data.RightPaneWidth - (float)e.Vector.X, 260, 380);
    }
}
