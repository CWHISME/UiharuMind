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

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        SettingConfig = SettingConfig;
    }

    public static readonly StyledProperty<object?> SettingConfigProperty =
        AvaloniaProperty.Register<SettingPanelView, object?>(nameof(SettingConfig),
            defaultBindingMode: BindingMode.TwoWay);

    public object? SettingConfig
    {
        get => GetValue(SettingConfigProperty);
        set
        {
            SetValue(SettingConfigProperty, value);

            var actualValue = SettingConfig;
            Title.Text = actualValue?.GetType().GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ??
                         actualValue?.GetType().Name;
            SettingListView.SettingConfig = actualValue;
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