using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace UiharuMind.Views.Controls;

public class ThemedIconButton : Ursa.Controls.IconButton
{
    protected ThemedSvgIcon? _icon;

    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<ThemedIconButton, string>(nameof(IconName));

    public static readonly StyledProperty<double> IconWidthProperty =
        AvaloniaProperty.Register<ThemedIconButton, double>(nameof(IconWidth), double.NaN);

    public static readonly StyledProperty<double> IconHeightProperty =
        AvaloniaProperty.Register<ThemedIconButton, double>(nameof(IconHeight), double.NaN);

    public static readonly StyledProperty<Color?> CurrentColorProperty =
        AvaloniaProperty.Register<ThemedIconButton, Color?>(nameof(CurrentColor));

    public static readonly StyledProperty<string?> ToolTipsProperty =
        AvaloniaProperty.Register<ThemedIconButton, string?>(nameof(ToolTips));

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    public double IconWidth
    {
        get => GetValue(IconWidthProperty);
        set => SetValue(IconWidthProperty, value);
    }

    public double IconHeight
    {
        get => GetValue(IconHeightProperty);
        set => SetValue(IconHeightProperty, value);
    }

    public Color? CurrentColor
    {
        get => GetValue(CurrentColorProperty);
        set => SetValue(CurrentColorProperty, value);
    }

    public string? ToolTips
    {
        get => GetValue(ToolTipsProperty);
        set => SetValue(ToolTipsProperty, value);
    }

    static ThemedIconButton()
    {
        IconNameProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnIconChanged());
        IconWidthProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnIconSizeChanged());
        IconHeightProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnIconSizeChanged());
        CurrentColorProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnCurrentColorChanged());
        ToolTipsProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnToolTipsChanged());
        BoundsProperty.Changed.AddClassHandler<ThemedIconButton>((btn, e) => btn.OnBoundsChanged());
    }

    private void OnBoundsChanged()
    {
        UpdateIconSize();
    }

    private double GetEffectiveIconWidth()
    {
        return double.IsNaN(IconWidth) ? Bounds.Width * 0.625f : IconWidth;
    }

    private double GetEffectiveIconHeight()
    {
        return double.IsNaN(IconHeight) ? Bounds.Height * 0.625f : IconHeight;
    }

    private void UpdateIconSize()
    {
        if (_icon == null) return;
        _icon.Width = GetEffectiveIconWidth();
        _icon.Height = GetEffectiveIconHeight();
    }

    private void OnIconChanged()
    {
        if (string.IsNullOrEmpty(IconName))
        {
            _icon = null;
            Icon = null;
            return;
        }

        _icon = new ThemedSvgIcon
        {
            IconName = IconName,
            Width = GetEffectiveIconWidth(),
            Height = GetEffectiveIconHeight()
        };
        var color = CurrentColor;
        if (color.HasValue)
            _icon.CurrentColor = color.Value;
        Icon = _icon;
    }

    private void OnIconSizeChanged()
    {
        UpdateIconSize();
    }

    private void OnCurrentColorChanged()
    {
        var color = CurrentColor;
        if (_icon != null && color.HasValue)
            _icon.CurrentColor = color.Value;
    }

    private void OnToolTipsChanged()
    {
        if (string.IsNullOrEmpty(ToolTips))
            ToolTip.SetTip(this, null);
        else
            ToolTip.SetTip(this, ToolTips);
    }
}