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
using Avalonia.Markup.Xaml;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 展示运行时引擎设置
/// </summary>
public partial class RuntimeEngineSettingView : UserControl
{
    public RuntimeEngineSettingView() : this(App.ViewModel.GetViewModel<SettingViewModel>().RuntimeEngineSettingData)
    {
    }

    /// <summary>
    /// 可注入页数据。无头 App 下 App.ViewModel 为 null，测试用它组装；生产仍走无参构造。
    /// </summary>
    /// <param name="viewModel">Runtime 页数据</param>
    public RuntimeEngineSettingView(RuntimeEngineSettingData viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnReleaseNotesClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is RuntimeEngineSettingData { EngineReleaseUrl: { } url })
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new System.Uri(url));
    }
}