using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class MainPageData : PageDataBase
{
    public string? Title { get; set; }
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;

    [RelayCommand]
    private void IncrementCount()
    {
        Count++;
        // LLamaCppManager.Instance.ScanModels();
    }

    protected override Control CreateView => new MainPage();
}