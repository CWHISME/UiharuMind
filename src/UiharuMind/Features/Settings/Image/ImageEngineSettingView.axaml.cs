using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Features.Settings.Image;

/// <summary>
/// 本地模型（生图）页
/// </summary>
public partial class ImageEngineSettingView : UserControl
{
    public ImageEngineSettingView() : this(App.ViewModel.GetViewModel<SettingViewModel>().ImageEngineSettingData)
    {
    }

    /// <param name="viewModel">页数据（无头测试自备）</param>
    public ImageEngineSettingView(ImageEngineSettingData viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    // 跳到主窗口模型页的「生图模型」页签
    private void OnManageModelsClick(object? sender, RoutedEventArgs e)
    {
        App.ShowMainPage<ModelPageData>(MenuPages.MenuModelKey)?.ShowImageModels();
    }
}
