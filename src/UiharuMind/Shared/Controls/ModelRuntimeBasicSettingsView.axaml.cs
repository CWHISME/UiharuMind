using Avalonia;
using Avalonia.Controls;
using UiharuMind.Shared.Data;

namespace UiharuMind.Shared.Controls;

public partial class ModelRuntimeBasicSettingsView : UserControl
{
    private const double CardMaxHeight = 460;
    private const double ContentMaxHeight = 245;

    /// <summary>
    /// 是否限制自身高度。默认 true：对话页小弹窗里用，超高部分走内部滚动。
    /// 设为 false：设置页里用，内容全展开，滚动交给外层页面，避免滚动套滚动裁内容。
    /// </summary>
    public static readonly StyledProperty<bool> IsHeightLimitedProperty =
        AvaloniaProperty.Register<ModelRuntimeBasicSettingsView, bool>(nameof(IsHeightLimited), true);

    public ModelRuntimeBasicSettingsView()
    {
        InitializeComponent();
        DataContext ??= new ModelRuntimeBasicSettingsData();
        ApplyHeightLimit();
    }

    /// <summary>是否限制自身高度，默认 true</summary>
    public bool IsHeightLimited
    {
        get => GetValue(IsHeightLimitedProperty);
        set => SetValue(IsHeightLimitedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsHeightLimitedProperty)
        {
            ApplyHeightLimit();
        }
    }

    private void ApplyHeightLimit()
    {
        if (OuterBorder is not null)
        {
            OuterBorder.MaxHeight = IsHeightLimited ? CardMaxHeight : double.PositiveInfinity;
        }

        if (ContentScroll is not null)
        {
            ContentScroll.MaxHeight = IsHeightLimited ? ContentMaxHeight : double.PositiveInfinity;
        }
    }
}
