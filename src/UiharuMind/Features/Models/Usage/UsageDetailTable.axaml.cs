using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 明细表。会话列是<b>可点的钻取入口</b>：点了在本页内切到该会话的命中率时间线，
/// 不跳对话页、不污染会话（设计文档 6.2）
/// </summary>
public partial class UsageDetailTable : UserControl
{
    public UsageDetailTable()
    {
        InitializeComponent();
    }

    private void OnSessionClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string sessionId }) return;
        if (DataContext is not UsagePageData data) return;
        data.DrillSessionCommand.Execute(sessionId);
    }
}
