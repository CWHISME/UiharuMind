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

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.Extensions;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 自动根据类的字段生成设置项
/// 建议代码创建使用
/// 注：只支持 ConfigBase 子类
/// </summary>
public partial class SettingListView : UserControl
{
    public SettingListView()
    {
        // DataContext = this;
        InitializeComponent();
    }

    private ConfigBase? _settingConfig;

    public object? SettingConfig
    {
        get => _settingConfig;
        set
        {
            if (_settingConfig == value)
            {
                RequestReloadValues();
                return;
            }

            if (_settingConfig != null) _settingConfig.PropertyChanged -= NotifyPropertyChanged;
            _settingConfig = value as ConfigBase;
            RefreshResetView();
            if (_settingConfig != null) _settingConfig.PropertyChanged += NotifyPropertyChanged;
        }
    }

    private bool _isVerticleTitle = false;

    public bool IsVerticleTitle
    {
        get => _isVerticleTitle;
        set
        {
            bool changed = _isVerticleTitle != value;
            _isVerticleTitle = value;
            if (changed) RefreshResetView();
        }
    }

    private void NotifyPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RequestReloadValues();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_settingConfig != null) _settingConfig.PropertyChanged -= NotifyPropertyChanged;
        if (_settingConfig != null) _settingConfig.PropertyChanged += NotifyPropertyChanged;
        RequestReloadValues();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (_settingConfig != null) _settingConfig.PropertyChanged -= NotifyPropertyChanged;
    }

    // public static readonly StyledProperty<object?> SettingConfigProperty =
    //     AvaloniaProperty.Register<SettingListView, object?>(nameof(SettingConfig));
    //
    // public object? SettingConfig
    // {
    //     get => GetValue(SettingConfigProperty);
    //     set
    //     {
    //         SetValue(SettingConfigProperty, value);
    //         RefreshResetView();
    //     }
    // }

    /// <summary>
    /// 通知重载数值
    /// </summary>
    public void RequestReloadValues()
    {
        foreach (var action in _changeActions)
        {
            action();
        }
    }

    private List<Action> _changeActions = new List<Action>();

    /// <summary>
    /// 刷新所有设置状态
    /// </summary>
    private void RefreshResetView()
    {
        if (SettingConfig == null) return;
        SettingContent.Children.Clear();
        _changeActions.Clear();
        foreach (var property in SettingConfig.GetType().GetProperties())
        {
            // var value = property.GetValue(SettingConfig);
            CreateControlForType(property.PropertyType, property);
        }
    }

    private void CreateControlForType(Type type, PropertyInfo property)
    {
        if (property.GetCustomAttribute<SettingConfigIgnoreDisplayAttribute>() != null) return;
        Control control;
        switch (type)
        {
            case not null when type == typeof(int):
            case not null when type == typeof(int?):
                control = CreateNumericControl(type, property, NumberStyles.Integer);
                break;
            case not null when type == typeof(long):
            case not null when type == typeof(long?):
                control = CreateNumericControl(type, property, NumberStyles.Integer);
                break;
            case not null when type == typeof(double):
            case not null when type == typeof(double?):
            case not null when type == typeof(float):
            case not null when type == typeof(float?):
                control = CreateNumericControl(type, property, NumberStyles.Float);
                break;
            case not null when type == typeof(string):
                control = CreateTextBoxControl(property);
                break;
            case not null when type == typeof(bool):
                control = CreateCheckBoxControl(property);
                break;
            // 其他类型处理
            default:
                Log.Error($"Type {type} is not supported.");
                return;
        }

        AddSettingControl(property, control);
    }

    //================基础组件 start=========================

    private Control CreateCheckBoxControl(PropertyInfo property)
    {
        var checkbox = new CheckBox
        {
            Content = property.Name,
            IsChecked = property.GetValue(SettingConfig) as bool?,
        };

        checkbox.IsCheckedChanged += (sender, e) =>
        {
            property.SetValue(SettingConfig, checkbox.IsChecked);
            NotifyPropertyChanged(property);
        };
        _changeActions.Add(() => checkbox.IsChecked = property.GetValue(SettingConfig) as bool?);
        return checkbox;
    }

    /// <summary>
    /// 数值设置，如果有设置范围，则使用滑块，否则使用数字输入框
    /// </summary>
    /// <param name="type"></param>
    /// <param name="property"></param>
    /// <param name="numberStyles"></param>
    /// <returns></returns>
    private Control CreateNumericControl(Type type, PropertyInfo property,
        NumberStyles numberStyles)
    {
        //设置过范围，使用 Slider
        SettingConfigRangeAttribute? range = property.GetCustomAttribute<SettingConfigRangeAttribute>();
        if (range != null)
        {
            DockPanel panel = new DockPanel() { LastChildFill = true };

            Slider slider = new Slider
            {
                Minimum = Convert.ToDouble(range.MinValue),
                Maximum = Convert.ToDouble(range.MaxValue),
                Value = Convert.ToDouble(property.GetValue(SettingConfig)),
                TickFrequency = Convert.ToDouble(range.Step),
                IsSnapToTickEnabled = true,
            };

            // Panel titlePanel = new Panel();
            TextBlock title = new TextBlock
            {
                Text = slider.Value.ToString("0.##"),
                Width = 50,
                // Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            // titlePanel.Children.Add(title);

            panel.Children.Add(title);
            panel.Children.Add(slider);
            DockPanel.SetDock(title, Dock.Right);
            DockPanel.SetDock(slider, Dock.Left);

            slider.ValueChanged += (sender, e) =>
            {
                property.SetValue(SettingConfig, slider.Value);
                title.Text = slider.Value.ToString("0.##");
                NotifyPropertyChanged(property);
            };

            _changeActions.Add(() => slider.Value = Convert.ToDouble(property.GetValue(SettingConfig)));

            return panel;
        }


        //没有设置范围，使用数字输入框
        var intbox = new NumericUpDown
        {
            Value = Convert.ToDecimal(property.GetValue(SettingConfig)),
            ParsingNumberStyle = numberStyles,
        };

        intbox.ValueChanged += (sender, e) =>
        {
            property.SetValue(SettingConfig, Convert.ToDouble(intbox.Value));
            NotifyPropertyChanged(property);
        };
        _changeActions.Add(() => intbox.Value = Convert.ToDecimal(property.GetValue(SettingConfig)));

        return intbox;
    }

    /// <summary>
    /// 文本输入框，如果有选项，则使用 ComboBox，否则使用普通输入框
    /// </summary>
    /// <param name="property"></param>
    /// <returns></returns>
    private Control CreateTextBoxControl(PropertyInfo property)
    {
        //设置选项
        SettingConfigOptionsAttribute? options = property.GetCustomAttribute<SettingConfigOptionsAttribute>();
        if (options != null)
        {
            ComboBox comboBox = new ComboBox();
            foreach (var option in options.Options)
            {
                comboBox.Items.Add(option);
            }

            comboBox.SelectedIndex = Array.IndexOf(options.Options, property.GetValue(SettingConfig)?.ToString());
            comboBox.SelectionChanged += (sender, e) =>
            {
                property.SetValue(SettingConfig, comboBox.SelectedItem);
                NotifyPropertyChanged(property);
            };
            _changeActions.Add(() =>
                comboBox.SelectedIndex = Array.IndexOf(options.Options, property.GetValue(SettingConfig)?.ToString()));

            return comboBox;
        }

        //没有设置选项，使用普通输入框
        var textbox = new TextBox
        {
            Text = property.GetValue(SettingConfig)?.ToString()
        };

        textbox.TextChanged += (sender, e) =>
        {
            property.SetValue(SettingConfig, textbox.Text);
            NotifyPropertyChanged(property);
        };
        _changeActions.Add(() => textbox.Text = property.GetValue(SettingConfig)?.ToString());

        return textbox;
    }

    //================基础组件 end========================

    private void AddSettingControl(PropertyInfo property, Control control)
    {
        //如果是复选框，不需要标题，直接添加
        if (control is CheckBox)
        {
            AddTooltip(control, property);
            DoAddSetting(control);
            return;
        }

        //设置输入排版
        control.HorizontalAlignment = HorizontalAlignment.Stretch;
        // control.HorizontalAlignment = HorizontalAlignment.Stretch;
        // control.Width = 300;

        // StackPanel panel = new StackPanel()
        // {
        //     Orientation = Orientation.Horizontal,
        //     HorizontalAlignment = HorizontalAlignment.Stretch,
        //     VerticalAlignment = VerticalAlignment.Center,
        //     Spacing = 10
        // };

        Panel panel = IsVerticleTitle
            ? new StackPanel()
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                // Spacing = 10
            }
            : new DockPanel() { LastChildFill = true };

        //添加设置项标题
        TextBlock title = new TextBlock
        {
            Text = property.Name,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Width = 120,
            // FontSize = 14,
            Margin = new Thickness(0, 0, 10, 0)
        };
        AddTooltip(title, property);
        panel.Children.Add(title);
        //添加设置项输入框
        panel.Children.Add(control);
        DoAddSetting(panel);
    }

    private void AddTooltip(Control control, PropertyInfo property)
    {
        ToolTip tip = new ToolTip
        {
            Content = property.GetDescription(),
        };
        ToolTip.SetTip(control, tip);
        ToolTip.SetShowDelay(control, 0);
    }

    private void DoAddSetting(Control control)
    {
        control.Margin = new Thickness(0, 5, 0, 5);
        SettingContent.Children.Add(control);
    }

    //NotifyPropertyChanged
    private void NotifyPropertyChanged(PropertyInfo property)
    {
        _settingConfig?.OnPropertyChanged(property.Name);
    }
}