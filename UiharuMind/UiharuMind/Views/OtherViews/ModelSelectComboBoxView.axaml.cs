using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.Views.OtherViews;

public partial class ModelSelectComboBoxView : UserControl
{
    public ModelSelectComboBoxView()
    {
        InitializeComponent();
        DataContext = App.ModelService;
    }

    private async void OnModelSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Log.Debug("Select Model: " + (e.AddedItems.Count > 0 ? e.AddedItems[0] : 0));
        // await App.ModelService.LoadModel((e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as ModelRunningData);
        App.ModelService.CurModelRunningData = (e.AddedItems.Count > 0 ? e.AddedItems[0] : null) as ModelRunningData;
    }
}