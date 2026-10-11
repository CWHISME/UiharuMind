using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models;

/// <summary>
/// 已装模型页签顶上的目录条：模型目录、更改（可选）、在资源管理器中显示、重新扫描，
/// 右侧先放各页签自己的按钮，再是共用的两条——获取模型与添加远程模型（给了命令才显示）
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

    public static readonly StyledProperty<ICommand?> GetModelsCommandProperty =
        AvaloniaProperty.Register<ModelFolderBar, ICommand?>(nameof(GetModelsCommand));

    public static readonly StyledProperty<ICommand?> AddRemoteCommandProperty =
        AvaloniaProperty.Register<ModelFolderBar, ICommand?>(nameof(AddRemoteCommand));

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
    /// 去「获取模型」页签；null 不显示。三个页签共用，排在 <see cref="Actions"/> 里的按钮右边
    /// </summary>
    public ICommand? GetModelsCommand
    {
        get => GetValue(GetModelsCommandProperty);
        set => SetValue(GetModelsCommandProperty, value);
    }

    /// <summary>
    /// 添加远程模型或在线服务；null 不显示。三个页签共用，排在 <see cref="GetModelsCommand"/> 右边
    /// </summary>
    public ICommand? AddRemoteCommand
    {
        get => GetValue(AddRemoteCommandProperty);
        set => SetValue(AddRemoteCommandProperty, value);
    }

    /// <summary>
    /// 这一页签自己的按钮（生图的画图），排在共用的两条左边
    /// </summary>
    public object? Actions
    {
        get => GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }
}
