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
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.Pages;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (DataContext is HomePageData data) data.UpdateResponsiveState(e.NewSize.Width);
    }

    private void OnLeftThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (HomePageData)DataContext!;
        data.DetailPaneWidth = Math.Clamp(data.DetailPaneWidth - (float)e.Vector.X, 280, 460);
    }
}
