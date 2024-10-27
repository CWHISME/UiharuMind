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
using UiharuMind.ViewModels.SettingViewData;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 用于快捷功能设置、包括界面等相关设置
/// </summary>
public partial class QuickToolSettingView : UserControl
{
    public QuickToolSettingView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<SettingViewModel>().QuickToolSettingConfig;
    }
}