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
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Services;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Extensions;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 通过反射获取并展示一个类的所有设置项的面板
/// 建议是代码创建使用，而不是在xaml中直接使用
/// </summary>
public partial class SettingPanelView : UserControl
{
    private object? _currentSettingConfig;

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

    public static readonly StyledProperty<bool> IsTitleVisibleProperty =
        AvaloniaProperty.Register<SettingPanelView, bool>(nameof(IsTitleVisible), true);

    /// <summary>
    /// 是否显示设置类自己的标题。宿主已经给了一行标题时关掉它，否则两行标题叠着
    /// （角色编辑页与会话详情栏都属于这种）
    /// </summary>
    public bool IsTitleVisible
    {
        get => GetValue(IsTitleVisibleProperty);
        set => SetValue(IsTitleVisibleProperty, value);
    }

    public static readonly StyledProperty<bool> IsVerticleTitleProperty =
        AvaloniaProperty.Register<SettingPanelView, bool>(nameof(IsVerticleTitle),
            defaultBindingMode: BindingMode.TwoWay);

    public bool IsVerticleTitle
    {
        get => GetValue(IsVerticleTitleProperty);
        set => SetValue(IsVerticleTitleProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == SettingConfigProperty)
        {
            _currentSettingConfig = change.NewValue;
            RefreshTitle();
            SettingListView.SettingConfig = change.NewValue;
        }
        else if (change.Property == IsVerticleTitleProperty)
        {
            SettingListView.IsVerticleTitle = change.GetNewValue<bool>();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        LocalizationManager.Instance.LanguageChanged -= RefreshLanguage;
        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;
        RefreshTitle();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        LocalizationManager.Instance.LanguageChanged -= RefreshLanguage;
    }

    private void RefreshLanguage()
    {
        RefreshTitle();
        SettingListView.SettingConfig = _currentSettingConfig;
    }

    //标题优先级:可本地化的 SettingConfigName → 老的 DisplayName → 类名(兜底,出现它说明这个设置类漏了标注)
    private void RefreshTitle()
    {
        var type = _currentSettingConfig?.GetType();
        if (type == null)
        {
            Title.Content = null;
            return;
        }

        string displayName = type.GetDisplayName();
        Title.Content = displayName != type.Name
            ? displayName
            : type.GetCustomAttribute<DisplayNameAttribute>()?.DisplayName ?? type.Name;
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