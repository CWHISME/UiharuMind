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
/// 模型 Shell 设置块。DataContext 由宿主显式给成 <see cref="ShellSettingsViewData"/>
/// </summary>
public partial class ShellSettingsView : UserControl
{
    public ShellSettingsView()
    {
        InitializeComponent();
    }
}
