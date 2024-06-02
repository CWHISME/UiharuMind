using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class MainPageData : PageDataBase
{
    public string? Title { get; set; } = "Model Viewer";
    public string? ModelPrefix { get; set; } = "Local models folder: ";
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;

    public ObservableCollection<GGufModelInfo> ModelSources { get; set; } = new ObservableCollection<GGufModelInfo>();

    [RelayCommand]
    private async Task OpenChangeModelPath()
    {
        LLamaConfig.LocalModelPath = await FilesService.OpenSelectFolderAsync(LLamaConfig.LocalModelPath)!;
        ModelPath = LLamaConfig.LocalModelPath;
        LLamaConfig.Save();
    }

    [RelayCommand]
    private void OpenModelFolder()
    {
        FilesService.OpenFolderAsync(LLamaConfig.LocalModelPath);
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
    }

    public override void OnEnable()
    {
        base.OnEnable();
        LoadModels();
    }

    protected override Control CreateView => new MainPage();

    private async void LoadModels()
    {
        IsBusy = true;
        ModelPath = LLamaConfig.LocalModelPath;
        var modelList = await LlamaService.GetModelList();
        ModelSources.Clear();
        foreach (var model in modelList)
        {
            ModelSources.Add(model);
        }

        ShowNotification("Model list updated.");
        IsBusy = false;
    }
}