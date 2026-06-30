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

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Views;
using UiharuMind.Views.Pages;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.Pages;

public partial class ModelPageData : PageDataBase
{
    private readonly IMessageService _messageService;
    // public string? Title { get; set; } = "Model Viewer";
    // public string? ModelPrefix { get; set; } = "Local models folder: ";
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;

    public ObservableCollection<ModelRunningData> ModelSources => App.ModelService.ModelSources;

    private LLamaCppSettingConfig LLamaConfig => LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.Config;

    public ModelPageData() : this(App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ModelPageData(IMessageService messageService)
    {
        _messageService = messageService;
    }

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
        _messageService.ShowNotification("Reload Info: " + path);
    }

    [RelayCommand]
    private void OpenSelectModelInfo(string path)
    {
        _messageService.ShowNotification("OpenSelectModelInfo.");
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

    [RelayCommand]
    private async Task DeleteRemoteModel(string name)
    {
        if (await _messageService.ConfirmAsync("Are you sure to delete remote model " + name + "?"))
        {
            LlmManager.Instance.RemoteModelManager.DeleteRemoteModel(name);
            LoadModels();
        }
    }

    [RelayCommand]
    private void SetFavoriteModel(string? name)
    {
        if (name == null) return;
        bool isRemove = ModelSettingConfig.Current.FavoriteModel == name;
        ModelSettingConfig.Current.FavoriteModel = isRemove ? "" : name;
        ModelSettingConfig.Current.Save();

        _messageService.ShowNotification(isRemove
            ? string.Format(Lang.FavoriteRemoteModelDelTips, name)
            : string.Format(Lang.FavoriteRemoteModelSetTips, name));
        LoadModels();
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
        _messageService.ShowNotification("Model list updated.");
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ModelPath = LLamaConfig.LocalModelPath;
    }

    protected override Control CreateView => new ModelPage();

    private async void LoadModels()
    {
        try
        {
            IsBusy = true;
            await App.ModelService.LoadModelList();
            // OnPropertyChanged(nameof(ModelSources));
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void UpdateModel(ModelRunningData model)
    {
        var index = ModelSources.IndexOf(model);
        if (index == -1) return;
        ModelSources[index] = model;
    }

}
