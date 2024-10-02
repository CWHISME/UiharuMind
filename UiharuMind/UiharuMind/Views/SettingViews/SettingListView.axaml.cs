using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 自动根据类的字段生成设置项
/// 建议代码创建使用
/// </summary>
public partial class SettingListView : UserControl
{
    public SettingListView()
    {
        // DataContext = this;
        InitializeComponent();
    }

    private object? _settingConfig;

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

            _settingConfig = value;
            RefreshResetView();
        }
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
        SettingContent.Children.Clear();
        _changeActions.Clear();
        if (SettingConfig is ConfigBase)
        {
            foreach (var property in SettingConfig.GetType().GetProperties())
            {
                // var value = property.GetValue(SettingConfig);
                CreateControlForType(property.PropertyType, property);
            }
        }
    }

    private void CreateControlForType(Type type, PropertyInfo property)
    {
        Control control;
        switch (type)
        {
            case not null when type == typeof(int):
                control = CreateNumericControl(type, property, NumberStyles.Integer);
                break;
            case not null when type == typeof(long):
                control = CreateNumericControl(type, property, NumberStyles.Integer);
                break;
            case not null when type == typeof(double):
                control = CreateNumericControl(type, property, NumberStyles.Float);
                break;
            case not null when type == typeof(float):
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

        checkbox.IsCheckedChanged += (sender, e) => { property.SetValue(SettingConfig, checkbox.IsChecked); };
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
            TextBlock title = new TextBlock
            {
                Text = slider.Value.ToString(CultureInfo.CurrentCulture),
                Margin = new Thickness(5, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };

            panel.Children.Add(title);
            panel.Children.Add(slider);
            DockPanel.SetDock(title, Dock.Right);
            DockPanel.SetDock(slider, Dock.Left);

            slider.ValueChanged += (sender, e) =>
            {
                property.SetValue(SettingConfig, Convert.ChangeType(slider.Value, type));
                title.Text = slider.Value.ToString(CultureInfo.CurrentCulture);
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
            property.SetValue(SettingConfig, Convert.ChangeType(intbox.Value, type));
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
            comboBox.SelectionChanged += (sender, e) => { property.SetValue(SettingConfig, comboBox.SelectedItem); };
            _changeActions.Add(() =>
                comboBox.SelectedIndex = Array.IndexOf(options.Options, property.GetValue(SettingConfig)?.ToString()));

            return comboBox;
        }

        //没有设置选项，使用普通输入框
        var textbox = new TextBox
        {
            Text = property.GetValue(SettingConfig)?.ToString()
        };

        textbox.TextChanged += (sender, e) => { property.SetValue(SettingConfig, textbox.Text); };
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
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.Width = 200;

        StackPanel panel = new StackPanel();
        //添加设置项标题
        TextBlock title = new TextBlock
        {
            Text = property.Name,
            // FontSize = 14,
            Margin = new Thickness(0, 0, 0, 5)
        };
        AddTooltip(title, property);
        panel.Children.Add(title);
        //添加设置项输入框
        panel.Children.Add(control);
        DoAddSetting(panel);
    }

    private void AddTooltip(Control control, PropertyInfo property)
    {
        var attributes = property.GetCustomAttributes<SettingConfigDescAttribute>();
        SettingConfigDescAttribute? selected = null;
        foreach (var attribute in attributes)
        {
            //多语言支持，如果没有找到匹配的语言，则使用英文
            if (attribute.LanguageCode == Lang.Culture.Name)
            {
                selected = attribute;
                break;
            }

            if (attribute.LanguageCode == LanguageUtils.EnglishUnitedStates && selected == null) selected = attribute;
        }

        if (selected == null) return;

        ToolTip tip = new ToolTip
        {
            Content = selected.Description
        };
        ToolTip.SetTip(control, tip);
    }

    private void DoAddSetting(Control control)
    {
        control.Margin = new Thickness(0, 5, 0, 5);
        SettingContent.Children.Add(control);
    }
}