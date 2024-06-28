using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class ModelPageData : PageDataBase
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
        FilesService.OpenFolder(LLamaConfig.LocalModelPath);
    }

    [RelayCommand]
    private void OpenSelectModelFolder(string path)
    {
        FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand]
    private async Task RefreshSelectModelInfo(string path)
    {
        await LlamaService.ScanLocalModel(path);
        ShowNotification("Reload Info：" + path);
    }

    [RelayCommand]
    private void OpenSelectModelInfo(string path)
    {
        ShowNotification("OpenSelectModelInfo.");
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

    protected override Control CreateView => new ModelPage();

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