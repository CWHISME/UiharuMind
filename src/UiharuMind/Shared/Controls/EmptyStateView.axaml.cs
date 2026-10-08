using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 列表空着时居中的那一块：图标、标题、说明，下面一排引导按钮
/// </summary>
public partial class EmptyStateView : UserControl
{
    public static readonly StyledProperty<string?> IconNameProperty =
        AvaloniaProperty.Register<EmptyStateView, string?>(nameof(IconName));

    public static readonly StyledProperty<string?> TitleProperty =
        AvaloniaProperty.Register<EmptyStateView, string?>(nameof(Title));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<EmptyStateView, string?>(nameof(Hint));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<EmptyStateView, object?>(nameof(Actions));

    public EmptyStateView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 图标（Assets/Icons 下的名字）
    /// </summary>
    public string? IconName
    {
        get => GetValue(IconNameProperty);
        set => SetValue(IconNameProperty, value);
    }

    /// <summary>
    /// 标题
    /// </summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>
    /// 说明
    /// </summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    /// <summary>
    /// 引导按钮
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
