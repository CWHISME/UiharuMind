using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace UiharuMind.Shared.Controls;

public class ThemedIconToggleButton : ThemedIconButton
{
    public static readonly StyledProperty<bool?> IsCheckedProperty =
        AvaloniaProperty.Register<ThemedIconToggleButton, bool?>(
            nameof(IsChecked),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<bool> IsThreeStateProperty =
        AvaloniaProperty.Register<ThemedIconToggleButton, bool>(nameof(IsThreeState));

    public static readonly RoutedEvent<RoutedEventArgs> IsCheckedChangedEvent =
        RoutedEvent.Register<ThemedIconToggleButton, RoutedEventArgs>(
            "IsCheckedChanged",
            RoutingStrategies.Bubble);

    /// <summary>
    /// 选中状态下的图标颜色
    /// </summary>
    public static readonly StyledProperty<IBrush?> CheckedForegroundProperty =
        AvaloniaProperty.Register<ThemedIconToggleButton, IBrush?>(nameof(CheckedForeground));

    /// <summary>
    /// 选中状态下的背景色
    /// </summary>
    public static readonly StyledProperty<IBrush?> CheckedBackgroundProperty =
        AvaloniaProperty.Register<ThemedIconToggleButton, IBrush?>(nameof(CheckedBackground));

    public ThemedIconToggleButton()
    {
        this.UpdatePseudoClasses(this.IsChecked);
    }

    /// <summary>
    /// 选中时的图标颜色
    /// </summary>
    public IBrush? CheckedForeground
    {
        get => GetValue(CheckedForegroundProperty);
        set => SetValue(CheckedForegroundProperty, value);
    }

    /// <summary>
    /// 选中时的背景色
    /// </summary>
    public IBrush? CheckedBackground
    {
        get => GetValue(CheckedBackgroundProperty);
        set => SetValue(CheckedBackgroundProperty, value);
    }

    public event EventHandler<RoutedEventArgs>? IsCheckedChanged
    {
        add => this.AddHandler<RoutedEventArgs>(ThemedIconToggleButton.IsCheckedChangedEvent, value);
        remove => this.RemoveHandler<RoutedEventArgs>(ThemedIconToggleButton.IsCheckedChangedEvent, value);
    }

    public bool? IsChecked
    {
        get => this.GetValue<bool?>(ThemedIconToggleButton.IsCheckedProperty);
        set => this.SetValue<bool?>(ThemedIconToggleButton.IsCheckedProperty, value);
    }

    public bool IsThreeState
    {
        get => this.GetValue<bool>(ThemedIconToggleButton.IsThreeStateProperty);
        set => this.SetValue<bool>(ThemedIconToggleButton.IsThreeStateProperty, value);
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);

        UpdateIconColor();
        UpdateBackground();
    }

    protected override void OnClick()
    {
        if (!this.IsEffectivelyEnabled)
            return;
        this.Toggle();
        base.OnClick();
    }

    protected virtual void Toggle()
    {
        bool? nullable = !this.IsChecked.HasValue
            ? new bool?(false)
            : (!this.IsChecked.Value
                ? new bool?(true)
                : (!this.IsThreeState ? new bool?(false) : new bool?()));

        this.SetCurrentValue<bool?>(ThemedIconToggleButton.IsCheckedProperty, nullable);
    }

    protected virtual void OnIsCheckedChanged(RoutedEventArgs e) => this.RaiseEvent(e);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsCheckedProperty)
        {
            this.OnIsCheckedChanged(new RoutedEventArgs(IsCheckedChangedEvent));
            UpdateIconColor();
            UpdateBackground();
            UpdatePseudoClasses(IsChecked);
        }
        else if (change.Property == CheckedForegroundProperty)
        {
            UpdateIconColor();
        }
        else if (change.Property == CheckedBackgroundProperty)
        {
            UpdateBackground();
        }
    }

    private void UpdateIconColor()
    {
        if (_icon == null) return;

        // 选中色 > 本按钮的 CurrentColor > 主题色。
        // 少了中间那档，选中态一取消就会跳回主题色，无视调用方设的 CurrentColor（深色工具条上表现为图标发黑）
        if (IsChecked == true && CheckedForeground is ISolidColorBrush checkedBrush)
        {
            _icon.CurrentColor = checkedBrush.Color;
        }
        else if (CurrentColor.HasValue)
        {
            _icon.CurrentColor = CurrentColor.Value;
        }
        else
        {
            // 恢复为主题色
            _icon.ClearValue(Avalonia.Svg.Skia.Svg.CurrentColorProperty);
            _icon.InvalidateVisual();
        }
    }

    private void UpdateBackground()
    {
        if (IsChecked == true && CheckedBackground != null)
        {
            Background = CheckedBackground;
        }
        else
        {
            ClearValue(BackgroundProperty);
        }
    }

    private void UpdatePseudoClasses(bool? isChecked)
    {
        PseudoClasses.Set(":checked", isChecked == true);
        PseudoClasses.Set(":unchecked", isChecked == false);
        PseudoClasses.Set(":indeterminate", isChecked == null);
    }
}