using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 「获取模型」各分类列表上方的当前下载源，点开下载源设置
/// </summary>
public partial class DownloadSourceButton : UserControl
{
    public static readonly StyledProperty<string?> LabelProperty =
        AvaloniaProperty.Register<DownloadSourceButton, string?>(nameof(Label));

    public static readonly StyledProperty<ICommand?> CommandProperty =
        AvaloniaProperty.Register<DownloadSourceButton, ICommand?>(nameof(Command));

    public DownloadSourceButton()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 「源：…」
    /// </summary>
    public string? Label
    {
        get => GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// 打开下载源设置
    /// </summary>
    public ICommand? Command
    {
        get => GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
