using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models;

/// <summary>
/// 已装模型页签顶上的目录条：模型目录、更改（可选）、在资源管理器中显示、重新扫描，右侧放页签自己的按钮
/// </summary>
public partial class ModelFolderBar : UserControl
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<ModelFolderBar, string?>(nameof(Path));

    public static readonly StyledProperty<ICommand?> ChangeCommandProperty =
        AvaloniaProperty.Register<ModelFolderBar, ICommand?>(nameof(ChangeCommand));

    public static readonly StyledProperty<ICommand?> OpenCommandProperty =
        AvaloniaProperty.Register<ModelFolderBar, ICommand?>(nameof(OpenCommand));

    public static readonly StyledProperty<ICommand?> RefreshCommandProperty =
        AvaloniaProperty.Register<ModelFolderBar, ICommand?>(nameof(RefreshCommand));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<ModelFolderBar, object?>(nameof(Actions));

    public ModelFolderBar()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 目录
    /// </summary>
    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    /// <summary>
    /// 更改目录；null 不显示
    /// </summary>
    public ICommand? ChangeCommand
    {
        get => GetValue(ChangeCommandProperty);
        set => SetValue(ChangeCommandProperty, value);
    }

    /// <summary>
    /// 在资源管理器中显示（参数为 null）
    /// </summary>
    public ICommand? OpenCommand
    {
        get => GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    /// <summary>
    /// 重新扫描
    /// </summary>
    public ICommand? RefreshCommand
    {
        get => GetValue(RefreshCommandProperty);
        set => SetValue(RefreshCommandProperty, value);
    }

    /// <summary>
    /// 右侧按钮
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
