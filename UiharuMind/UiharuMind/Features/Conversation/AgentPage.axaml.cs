/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using Avalonia.Input;

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

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        ((AgentPageData)DataContext!).DragLeftPane(e.Vector.X);
    }

    private void OnRightThumbDragDelta(object? sender, VectorEventArgs e)
    {
        ((AgentPageData)DataContext!).DragRightPane(e.Vector.X);
    }
}