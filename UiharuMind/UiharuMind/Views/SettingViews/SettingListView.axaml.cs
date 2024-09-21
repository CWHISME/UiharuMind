using System;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Views.SettingViews;

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
    /// 刷新所有设置状态
    /// </summary>
    private void RefreshResetView()
    {
        SettingContent.Children.Clear();
        if (SettingConfig is ConfigBase)
        {
            foreach (var property in SettingConfig.GetType().GetProperties())
            {
                var value = property.GetValue(SettingConfig);

                CreateControlForType(property.PropertyType, value, property);
            }
        }
    }

    private void CreateControlForType(Type type, object? value, PropertyInfo property)
    {
        Control control;
        switch (type)
        {
            case not null when type == typeof(int):
                control = CreateNumericUpDownControl(type, Convert.ToDecimal(value), property, NumberStyles.Integer);
                break;
            case not null when type == typeof(long):
                control = CreateNumericUpDownControl(type, Convert.ToDecimal(value), property, NumberStyles.Integer);
                break;
            case not null when type == typeof(double):
                control = CreateNumericUpDownControl(type, Convert.ToDecimal(value), property, NumberStyles.Float);
                break;
            case not null when type == typeof(float):
                control = CreateNumericUpDownControl(type, Convert.ToDecimal(value), property, NumberStyles.Float);
                break;
            case not null when type == typeof(string):
                control = CreateTextBoxControl(value as string, property);
                break;
            case not null when type == typeof(bool):
                control = CreateCheckBoxControl(value as bool?, property);
                break;
            // 其他类型处理
            default:
                Log.Error($"Type {type} is not supported.");
                return;
        }

        AddSettingControl(property, control);
    }

    private Control CreateCheckBoxControl(bool? value, PropertyInfo property)
    {
        var checkbox = new CheckBox
        {
            Content = property.Name,
            IsChecked = value
        };

        checkbox.IsCheckedChanged += (sender, e) => { property.SetValue(SettingConfig, true); };
        return checkbox;
    }

    private NumericUpDown CreateNumericUpDownControl(Type type, object? initialValue, PropertyInfo property,
        NumberStyles numberStyles)
    {
        var intbox = new NumericUpDown
        {
            Value = initialValue as decimal?,
            ParsingNumberStyle = numberStyles,
        };

        intbox.ValueChanged += (sender, e) =>
        {
            property.SetValue(SettingConfig, Convert.ChangeType(intbox.Value, type));
        };

        return intbox;
    }

    private TextBox CreateTextBoxControl(string? initialValue, PropertyInfo property)
    {
        var textbox = new TextBox
        {
            Text = initialValue
        };

        textbox.TextChanged += (sender, e) => { property.SetValue(SettingConfig, textbox.Text); };

        return textbox;
    }

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
        string? tooltip = property.GetCustomAttribute<SettingConfigDescAttribute>()?.Description;
        if (tooltip == null) return;

        ToolTip tip = new ToolTip();
        tip.Content = tooltip;
        ToolTip.SetTip(control, tip);
    }

    private void DoAddSetting(Control control)
    {
        control.Margin = new Thickness(0, 5, 0, 5);
        SettingContent.Children.Add(control);
    }
}