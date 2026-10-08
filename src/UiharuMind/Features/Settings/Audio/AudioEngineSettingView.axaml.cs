using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 本地模型（语音）页
/// </summary>
public partial class AudioEngineSettingView : UserControl
{
    public AudioEngineSettingView() : this(App.ViewModel.GetViewModel<SettingViewModel>().AudioEngineSettingData)
    {
    }

    /// <param name="viewModel">页数据（无头测试自备）</param>
    public AudioEngineSettingView(AudioEngineSettingData viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void OnDownloadSourceClick(object? sender, RoutedEventArgs e)
    {
        this.FindAncestorOfType<SettingsWindow>()?.ShowDownloadSourceSettings();
    }
}
