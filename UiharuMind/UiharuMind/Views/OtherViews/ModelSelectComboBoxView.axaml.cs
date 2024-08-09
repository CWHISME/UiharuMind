using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.OtherViews;

public partial class ModelSelectComboBoxView : UserControl
{
    public ModelSelectComboBoxView()
    {
        InitializeComponent();
        var data = App.DummyWindow.MainViewModel.GetPage(MenuKeys.MenuModelKey) as ModelPageData;
        data?.LoadModels();
        DataContext = data;
    }

    private void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        
    }
}