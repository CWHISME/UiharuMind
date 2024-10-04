using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData.ClipboardViewData;

namespace UiharuMind.Views.SettingViews;

public partial class ClipboardSettingView : UserControl
{
    public ClipboardSettingView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ClipboardSettingViewModel>();
    }
}