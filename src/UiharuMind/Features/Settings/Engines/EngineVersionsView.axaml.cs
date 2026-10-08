using Avalonia.Controls;
using Avalonia.Interactivity;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 引擎版本卡（文本页与语音页共用），数据见 <see cref="EngineVersionsViewData"/>
/// </summary>
public partial class EngineVersionsView : UserControl
{
    public EngineVersionsView()
    {
        InitializeComponent();
    }

    private void OnReleaseNotesClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is EngineVersionsViewData { ReleaseUrl: { } url })
            _ = TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new System.Uri(url));
    }
}
