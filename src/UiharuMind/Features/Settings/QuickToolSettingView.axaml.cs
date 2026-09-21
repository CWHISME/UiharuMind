/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 快捷工具设置页：文本类/视觉类默认模型两个下拉。
/// </summary>
public partial class QuickToolSettingView : UserControl
{
    public QuickToolSettingView()
    {
        InitializeComponent();
        DataContext = new QuickToolSettingViewData();
    }
}
