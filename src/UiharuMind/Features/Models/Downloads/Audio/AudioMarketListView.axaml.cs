using Avalonia.Controls;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型的左栏：搜索、按任务筛选与家族列表；没装引擎时换成安装引导
/// </summary>
public partial class AudioMarketListView : UserControl
{
    public AudioMarketListView()
    {
        InitializeComponent();
    }
}
