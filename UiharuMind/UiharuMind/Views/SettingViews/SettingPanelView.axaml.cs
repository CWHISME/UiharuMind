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

using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 通过反射获取并展示一个类的所有设置项的面板
/// 建议是代码创建使用，而不是在xaml中直接使用
/// </summary>
public partial class SettingPanelView : UserControl
{
    public SettingPanelView()
    {
        // DataContext = this;
        InitializeComponent();
    }

    // protected override void OnLoaded(RoutedEventArgs e)
    // {
    //     base.OnLoaded(e);
    //     SettingConfig = SettingConfig;
    // }

    public static readonly StyledProperty<object?> SettingConfigProperty =
        AvaloniaProperty.Register<SettingPanelView, object?>(nameof(SettingConfig),
            defaultBindingMode: BindingMode.TwoWay);

    public object? SettingConfig
    {
        get => GetValue(SettingConfigProperty);
        set => SetValue(SettingConfigProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingConfigProperty)
        {
            var actualValue = change.NewValue;
            Title.Text = actualValue?.GetType().GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ??
                         actualValue?.GetType().Name;
            SettingListView.SettingConfig = change.NewValue;
        }
    }
    // private object? _settingConfig;
    //
    // public object? SettingConfig
    // {
    //     get => _settingConfig;
    //     set
    //     {
    //         _settingConfig = value;
    //         Title.Text = _settingConfig?.GetType().GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ??
    //                      _settingConfig?.GetType().Name;
    //         SettingListView.SettingConfig = _settingConfig;
    //     }
    // }
}