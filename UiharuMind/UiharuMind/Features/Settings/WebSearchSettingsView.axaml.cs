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
/// 联网搜索设置块：API 凭据 + 搜索链健康状况。
/// DataContext 由宿主显式给成 <see cref="WebSearchSettingsViewData"/>，不走逻辑树继承——
/// 模板里的 <c>$parent[ItemsControl].DataContext</c> 强转依赖这一点。
/// </summary>
public partial class WebSearchSettingsView : UserControl
{
    public WebSearchSettingsView()
    {
        InitializeComponent();
    }
}
