using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 一行可下载项（<see cref="DownloadRowDataBase"/>）：风险点、名字与推荐、进度与状态、大小；
/// 名字旁的徽标与右侧按钮由各分类塞进 <see cref="Badges"/> / <see cref="Actions"/>
/// </summary>
public partial class DownloadRowView : UserControl
{
    public static readonly StyledProperty<object?> BadgesProperty =
        AvaloniaProperty.Register<DownloadRowView, object?>(nameof(Badges));

    public static readonly StyledProperty<object?> ActionsProperty =
        AvaloniaProperty.Register<DownloadRowView, object?>(nameof(Actions));

    public DownloadRowView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 名字旁的额外徽标
    /// </summary>
    public object? Badges
    {
        get => GetValue(BadgesProperty);
        set => SetValue(BadgesProperty, value);
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
