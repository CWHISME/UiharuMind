using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 模型状态徽章组：远程、视觉。两个都不亮时整个控件折叠，不在外层 StackPanel 里白占一格间距
/// </summary>
public partial class ModelStateBadges : UserControl
{
    /// <summary>是不是远程模型</summary>
    public static readonly StyledProperty<bool> IsRemoteProperty =
        AvaloniaProperty.Register<ModelStateBadges, bool>(nameof(IsRemote));

    /// <summary>是不是视觉模型</summary>
    public static readonly StyledProperty<bool> IsVisionProperty =
        AvaloniaProperty.Register<ModelStateBadges, bool>(nameof(IsVision));

    /// <summary>徽章之间的间距</summary>
    public static readonly StyledProperty<double> SpacingProperty =
        AvaloniaProperty.Register<ModelStateBadges, double>(nameof(Spacing), 5);

    static ModelStateBadges()
    {
        IsRemoteProperty.Changed.AddClassHandler<ModelStateBadges>((x, _) => x.SyncVisible());
        IsVisionProperty.Changed.AddClassHandler<ModelStateBadges>((x, _) => x.SyncVisible());
    }

    /// <summary>加载布局</summary>
    public ModelStateBadges()
    {
        InitializeComponent();
        SyncVisible();
    }

    /// <summary>是不是远程模型</summary>
    public bool IsRemote
    {
        get => GetValue(IsRemoteProperty);
        set => SetValue(IsRemoteProperty, value);
    }

    /// <summary>是不是视觉模型</summary>
    public bool IsVision
    {
        get => GetValue(IsVisionProperty);
        set => SetValue(IsVisionProperty, value);
    }

    /// <summary>徽章之间的间距</summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    private void SyncVisible() => IsVisible = IsRemote || IsVision;
}
