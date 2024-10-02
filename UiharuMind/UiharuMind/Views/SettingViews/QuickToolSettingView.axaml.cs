using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.SettingViewData;

namespace UiharuMind.Views.SettingViews;

/// <summary>
/// 用于快捷功能设置、包括界面等相关设置
/// </summary>
public partial class QuickToolSettingView : UserControl
{
    public QuickToolSettingView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<QuickToolSettingViewModel>();
    }
}