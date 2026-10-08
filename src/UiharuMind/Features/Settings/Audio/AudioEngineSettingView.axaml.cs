using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Shell;

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

    // 跳到主窗口模型页的「语音模型」页签
    private void OnManageModelsClick(object? sender, RoutedEventArgs e)
    {
        App.ShowMainPage<ModelPageData>(MenuPages.MenuModelKey)?.ShowAudioModels();
    }
}
