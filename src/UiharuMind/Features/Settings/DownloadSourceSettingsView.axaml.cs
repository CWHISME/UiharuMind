using Avalonia.Controls;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 下载源页：模型从哪下、GitHub 代理。本地模型（文本 / 语音）与引擎包共用
/// </summary>
public partial class DownloadSourceSettingsView : UserControl
{
    public DownloadSourceSettingsView() : this(new DownloadSourceSettingsViewData())
    {
    }

    /// <param name="viewModel">页数据（测试传独立配置）</param>
    public DownloadSourceSettingsView(DownloadSourceSettingsViewData viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
