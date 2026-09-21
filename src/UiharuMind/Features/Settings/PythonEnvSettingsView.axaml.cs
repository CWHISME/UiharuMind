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
/// 受管 Python 环境设置块。DataContext 由宿主显式给成 <see cref="PythonEnvSettingsViewData"/>
/// </summary>
public partial class PythonEnvSettingsView : UserControl
{
    public PythonEnvSettingsView()
    {
        InitializeComponent();
    }
}
