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

        // 如果已选中且有指定的选中颜色，则使用选中颜色
        // 否则使用默认颜色（ThemedIconButton 的 CurrentColor）
        if (IsChecked == true && CheckedForeground != null)
        {
            // 将 IBrush 转换为 Color
            if (CheckedForeground is ISolidColorBrush solidBrush)
            {
                _icon.CurrentColor = solidBrush.Color;
            }
        }
        else
        {
            // 恢复为默认颜色
            _icon.ClearValue(Avalonia.Svg.Skia.Svg.CurrentColorProperty);;

            // 强制重新应用主题颜色
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