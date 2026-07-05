using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using UiharuMind.Services;

namespace UiharuMind.Views.Controls;

public class ThemedSvgIcon : global::Avalonia.Svg.Skia.Svg
{
    private const string SvgPathFormat = "avares://UiharuMind/Assets/Icons/{0}.svg";

    public static readonly StyledProperty<string> IconNameProperty =
        AvaloniaProperty.Register<ThemedSvgIcon, string>(nameof(IconName));

    public string IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    static ThemedSvgIcon()
    {
        IconNameProperty.Changed.AddClassHandler<ThemedSvgIcon>((icon, e) =>
            icon.OnIconNameChanged());
    }

    public ThemedSvgIcon() : base((Uri?)null)
    {
    }

    public ThemedSvgIcon(IServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IconNameProperty)
        {
            UpdateIconPath();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateIconPath();
        UpdateCurrentColor();
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged += OnThemeChanged;
        }
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is { } app)
        {
            app.ActualThemeVariantChanged -= OnThemeChanged;
        }
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateCurrentColor();
    }

    private void OnIconNameChanged()
    {
        UpdateIconPath();
    }

    private void UpdateIconPath()
    {
        if (!string.IsNullOrEmpty(IconName))
        {
            Path = string.Format(SvgPathFormat, IconName);
        }
    }

    private void UpdateCurrentColor()
    {
        CurrentColor = ApplicationThemeManager.IsDarkTheme()
            ? Colors.White
            : Colors.Black;
    }
}
