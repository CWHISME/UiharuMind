/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UiharuMind.Features.Settings;

public partial class McpSettingsView : UserControl
{
    public McpSettingsView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 每次显示时重取已授权项目清单。
    ///
    /// 必须挂在这里：授权是在<b>会话侧</b>选定工作区那一刻给出的，而这个视图模型由
    /// <c>AgentSettingViewData</c> 持有、只构造一次——只在构造时取一次的话，
    /// 你刚授权过的那个项目在设置页里永远不出现，看着就像这份清单没生效。
    /// </summary>
    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        (DataContext as McpSettingsViewData)?.RefreshTrustedWorkspaces();
    }
}
