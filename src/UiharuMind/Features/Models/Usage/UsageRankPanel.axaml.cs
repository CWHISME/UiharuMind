using Avalonia.Controls;

namespace UiharuMind.Features.Models.Usage;

public partial class UsageRankPanel : UserControl
{
    public UsageRankPanel()
    {
        InitializeComponent();
        // 默认的提示跟手，压在扇区上；换成固定右上角的那版（见 UsagePieTooltip）
        ShareChart.Tooltip = new UsagePieTooltip();
    }
}
