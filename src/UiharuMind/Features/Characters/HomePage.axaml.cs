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
using Avalonia.Input;

namespace UiharuMind.Features.Characters;

public partial class HomePage : UserControl
{
    public HomePage()
    {
        InitializeComponent();
    }

    private void OnListThumbDragDelta(object? sender, VectorEventArgs e)
    {
        var data = (HomePageData)DataContext!;
        // 下限按"名字 + 徽章"这一行的最小可读宽度定;上限只是别把编辑器挤没
        data.ListPaneWidth = Math.Clamp(data.ListPaneWidth + (float)e.Vector.X, 220, 420);
    }
}
