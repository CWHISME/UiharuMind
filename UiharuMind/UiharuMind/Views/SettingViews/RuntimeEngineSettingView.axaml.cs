using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.SettingViewData;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 展示运行时引擎设置
/// </summary>
public partial class RuntimeEngineSettingView : UserControl
{
    public RuntimeEngineSettingView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<RuntimeEngineSettingModel>();
    }
}