using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Svg.Skia;
using Avalonia.Threading;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Controls;

public class ThemedSvgIcon : global::Avalonia.Svg.Skia.Svg
{
    private const string SvgPathFormat = "avares://UiharuMind/Assets/Icons/{0}.svg";

    // 标志位：标记 CurrentColor 是否由外部（用户绑定/赋值）设置
    private bool _externalColorSet;

    // 防止递归的标志
    private bool _isUpdatingCurrentColor;

    private bool _isPathScheduled; //路径已排到下一拍设

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

    public ThemedSvgIcon() : base(new Uri("avares://UiharuMind/Assets/Icons"))
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
            if (VisualRoot != null) ScheduleIconPath();
        }
        else if (change.Property == CurrentColorProperty && !_isUpdatingCurrentColor)
        {
            // 任何非内部引发的 CurrentColor 变化都视为外部设置
            _externalColorSet = change.NewValue != null;
            UpdateCurrentColor();
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateCurrentColor();
        ScheduleIconPath();
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged += OnThemeChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        if (Application.Current is { } app)
            app.ActualThemeVariantChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        UpdateCurrentColor();
    }

    private void OnIconNameChanged()
    {
        if (VisualRoot != null) ScheduleIconPath();
    }

    // 库每改一次 Path / CurrentColor 都取消在途加载重来一遍，每次取消抛一串异常（挂调试器时是秒级卡顿）。
    // 所以挂上界面后晚一拍才设路径：宿主模板里按选中态设颜色之类的都落定了，每个图标只加载一次
    private void ScheduleIconPath()
    {
        if (_isPathScheduled) return;
        _isPathScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isPathScheduled = false;
            if (VisualRoot != null) UpdateIconPath();
        }, DispatcherPriority.Loaded);
    }

    private void UpdateIconPath()
    {
        if (!string.IsNullOrEmpty(IconName))
            Path = string.Format(SvgPathFormat, IconName);
    }

    private void UpdateCurrentColor()
    {
        // 如果已显式设置过颜色，则不再自动覆盖
        if (_externalColorSet || _isUpdatingCurrentColor)
            return;

        _isUpdatingCurrentColor = true;
        CurrentColor = ApplicationThemeManager.IsDarkTheme() ? Colors.White : Colors.Black;
        _isUpdatingCurrentColor = false;
    }
}