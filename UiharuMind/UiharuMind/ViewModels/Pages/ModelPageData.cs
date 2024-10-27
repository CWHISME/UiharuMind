/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.LLamaCpp.Data;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Views;
using UiharuMind.Views.Pages;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.Pages;

public partial class ModelPageData : PageDataBase
{
    public string? Title { get; set; } = "Model Viewer";
    public string? ModelPrefix { get; set; } = "Local models folder: ";
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;

    public ObservableCollection<ModelRunningData> ModelSources => App.ModelService.ModelSources;

    private LLamaCppSettingConfig LLamaConfig => LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.Config;

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

    [RelayCommand]
    private async Task CreateRemoteModel(string? name)
    {
        RemoteModelInfo? info = null;
        if (name != null) LlmManager.Instance.RemoteModelManager.Config.ModelInfos.TryGetValue(name, out info);
        var model = await CreateRemoteLlmModelWindow.ShowWindow(UIManager.GetRootWindow(), info);
        if (model != null)
        {
            LlmManager.Instance.RemoteModelManager.AddRemoteModel(model);
            LoadModels();
        }
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
        // OnPropertyChanged(nameof(ModelSources));
        IsBusy = false;
    }
}