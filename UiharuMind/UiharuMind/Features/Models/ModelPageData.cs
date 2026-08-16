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
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Features.Models;

public partial class ModelPageData : PageDataBase
{
    private readonly IMessageService _messageService;
    // public string? Title { get; set; } = "Model Viewer";
    // public string? ModelPrefix { get; set; } = "Local models folder: ";
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _count;

    public ObservableCollection<ModelRunningData> ModelSources => App.ModelService.ModelSources;
    
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
        ModelSettingConfig.Current.LocalModelPath = await App.FilesService.OpenSelectFolderAsync(ModelSettingConfig.Current.LocalModelPath)!;
        ModelPath = ModelSettingConfig.Current.LocalModelPath;
        ModelSettingConfig.Current.Save();
    }

    [RelayCommand]
    private void OpenModelFolder()
    {
        App.FilesService.OpenFolder(ModelSettingConfig.Current.LocalModelPath);
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
    private async Task OpenSelectModelInfo(string path)
    {
        GGufModelInfo? info = ModelSources
            .Select(model => model.ModelInfo)
            .OfType<GGufModelInfo>()
            .FirstOrDefault(model => model.ModelPath == path);
        if (info == null)
        {
            _messageService.ShowNotification(path);
            return;
        }

        string message = string.Join(Environment.NewLine, new[]
        {
            $"Name: {info.DisplayName}",
            $"Architecture: {info.Architecture}",
            $"Size: {info.SizeLabel}",
            $"Context: {info.ContextLength:N0}",
            $"Embedding: {info.EmbeddingLength:N0}",
            $"Layers: {info.LayerCount}",
            $"Heads: {info.AttentionHeadCount} / KV {info.AttentionHeadCountKv}",
            $"File: {info.ModelPath}"
        }.Where(line => !line.EndsWith(": ", StringComparison.Ordinal)));

        await _messageService.ShowInfoAsync(message, info.ModelName);
    }

    [RelayCommand]
    private async Task CreateRemoteModel(string? name)
    {
        RemoteModelInfo? info = null;
        if (name != null) LlmManager.Instance.TryGetRemoteModelInfo(name, out info);
        var model = await CreateRemoteLlmModelWindow.ShowWindow(UIManager.GetRootWindow(), info);
        if (model != null)
        {
            LlmManager.Instance.AddRemoteModel(model);
            LoadModels();
        }
    }

    [RelayCommand]
    private async Task DeleteRemoteModel(string name)
    {
        if (await _messageService.ConfirmAsync("Are you sure to delete remote model " + name + "?"))
        {
            LlmManager.Instance.DeleteRemoteModel(name);
            LoadModels();
        }
    }

    [RelayCommand]
    private void SetFavoriteModel(string? name)
    {
        if (name == null) return;
        bool isRemove = ModelSettingConfig.Current.IsFavorite(name);
        ModelSettingConfig.Current.ToggleFavorite(name);
        ModelSettingConfig.Current.Save();

        _messageService.ShowNotification(isRemove
            ? string.Format(Lang.FavoriteRemoteModelDelTips, name)
            : string.Format(Lang.FavoriteRemoteModelSetTips, name));
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
        _messageService.ShowNotification("Model list updated.");
    }

    public override void OnEnable()
    {
        base.OnEnable();
        ModelPath = ModelSettingConfig.Current.LocalModelPath;
    }

    protected override Control CreateView => new ModelPage();

    // 原先是 async void：catch 之外再漏一个异常就是进程级崩溃。改成即发即忘，忙标志与日志交给作用域
    private void LoadModels()
    {
        _ = AsyncCommandScope.RunAsync(v => IsBusy = v, App.ModelService.LoadModelList);
    }

    private void UpdateModel(ModelRunningData model)
    {
        var index = ModelSources.IndexOf(model);
        if (index == -1) return;
        ModelSources[index] = model;
    }

}
