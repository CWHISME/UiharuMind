using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
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

    public ObservableCollection<ModelRunningData> ModelSources => App.ModelService.ModelSources;

    private LLamaCppSettingConfig LLamaConfig => LlmManager.Instance.LLamaCppServer.Config;

    [RelayCommand]
    private async Task OpenChangeModelPath()
    {
        LLamaConfig.LocalModelPath = await App.FilesService.OpenSelectFolderAsync(LLamaConfig.LocalModelPath)!;
        ModelPath = LLamaConfig.LocalModelPath;
        LLamaConfig.Save();
    }

    [RelayCommand]
    private void OpenModelFolder()
    {
        App.FilesService.OpenFolder(LLamaConfig.LocalModelPath);
    }

    [RelayCommand]
    private void OpenSelectModelFolder(string path)
    {
        App.FilesService.OpenFolder(Path.GetDirectoryName(path) ?? path);
    }

    [RelayCommand]
    private async Task RefreshSelectModelInfo(string path)
    {
        await App.ModelService.LoadModelList();
        App.MessageService.ShowNotification("Reload Info：" + path);
    }

    [RelayCommand]
    private void OpenSelectModelInfo(string path)
    {
        App.MessageService.ShowNotification("OpenSelectModelInfo.");
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
        App.MessageService.ShowNotification("Model list updated.");
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ModelPath = LLamaConfig.LocalModelPath;
    }

    protected override Control CreateView => new ModelPage();

    public async void LoadModels()
    {
        IsBusy = true;
        await App.ModelService.LoadModelList();
        IsBusy = false;
    }
}