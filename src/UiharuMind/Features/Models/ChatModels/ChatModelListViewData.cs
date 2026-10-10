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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Features.Models;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.WindowManagement;

namespace UiharuMind.Features.Models.ChatModels;

/// <summary>
/// 「对话模型」页签：本地模型目录与列表（本地、远程），收藏、删除、参数与信息
/// </summary>
public partial class ChatModelListViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly Action _goToDownloads;
    private readonly ObservableCollection<ModelRunningData>? _modelSources; //null 走 App.ModelService
    private bool _isWatchingSources;
    private bool _isSyncingModelPath; //切页对齐路径期间抑制提示
    private bool _isListSchedulePending; //防抖：同一拍里多次启用只排一次点亮
    [ObservableProperty] private string? _modelPath;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isListDataReady;

    /// <param name="messageService">提示与通知</param>
    /// <param name="goToDownloads">空状态里「去获取模型」</param>
    /// <param name="modelSources">对话模型列表，默认 App.ModelService 的</param>
    /// <param name="engineStatus">本地引擎的状态条；null 不显示</param>
    public ChatModelListViewData(IMessageService messageService, Action goToDownloads,
        ObservableCollection<ModelRunningData>? modelSources = null, EngineStatusData? engineStatus = null)
    {
        _messageService = messageService;
        _goToDownloads = goToDownloads;
        _modelSources = modelSources;
        EngineStatus = engineStatus;
    }

    /// <summary>本地引擎的状态</summary>
    public EngineStatusData? EngineStatus { get; }

    public ObservableCollection<ModelRunningData> ModelSources => _modelSources ?? App.ModelService.ModelSources;

    /// <summary>
    /// 列表亮起后还一个模型都没有：显示空状态，引去获取模型或添加远程模型
    /// </summary>
    public bool HasNoModels => IsListDataReady && ModelSources.Count == 0;

    /// <summary>
    /// 切页首帧只出页面骨架，条目列表推迟一拍再挂载。
    /// 原因：ListBox 在切页同步布局里会一次性物化全部条目（实测 16 条 ~80ms），
    /// 直接绑 <see cref="ModelSources"/> 会把这笔账压进点击路径；
    /// 先绑空集合让骨架 40ms 内出画，下一拍再换全集合并让引擎自然布局。
    /// </summary>
    public IEnumerable<ModelRunningData> CurrentItems =>
        IsListDataReady ? ModelSources : Array.Empty<ModelRunningData>();

    partial void OnIsListDataReadyChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentItems));
        OnPropertyChanged(nameof(HasNoModels));
        if (!value || _isWatchingSources) return;
        _isWatchingSources = true;
        ModelSources.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasNoModels));
    }

    /// <summary>
    /// 页面显示时调：骨架先行，下一拍再亮列表；模型目录在别处改过就对齐
    /// </summary>
    public void Enable()
    {
        // 首帧先不亮列表，下一拍再挂全集合并让引擎出画——否则 ListBox 一次性物化会卡在点击路径上（见 CurrentItems 注释）
        if (!IsListDataReady && !_isListSchedulePending)
        {
            _isListSchedulePending = true;
            Dispatcher.UIThread.Post(() =>
            {
                _isListSchedulePending = false;
                IsListDataReady = true;
            }, DispatcherPriority.Background);
        }

        if (ModelPath == ModelSettingConfig.Current.LocalModelPath) return;

        _isSyncingModelPath = true;
        try
        {
            ModelPath = ModelSettingConfig.Current.LocalModelPath;
        }
        finally
        {
            _isSyncingModelPath = false;
        }
    }

    [RelayCommand]
    private async Task OpenChangeModelPath()
    {
        string selected = await App.FilesService.OpenSelectFolderAsync(ModelSettingConfig.Current.LocalModelPath);
        if (string.IsNullOrEmpty(selected)) return; //取消选择
        ModelSettingConfig.Current.LocalModelPath = selected;
        ModelPath = selected;
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
    private async Task RefreshModels()
    {
        await AsyncCommandScope.RunAsync(v => IsBusy = v, App.ModelService.LoadModelList);
        _messageService.ShowNotification(Loc.Text(LangKey.ModelListUpdated));
    }

    [RelayCommand]
    private void GoToDownloads() => _goToDownloads();

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
        if (await _messageService.ConfirmAsync(Loc.Text(LangKey.RemoteModelDeleteConfirm, name)))
        {
            LlmManager.Instance.DeleteRemoteModel(name);
            LoadModels();
        }
    }

    /// <summary>
    /// 随包内置的模型不给删
    /// </summary>
    /// <param name="model">模型</param>
    /// <returns>可删返回 true</returns>
    public static bool CanDeleteLocalModel(ModelRunningData model)
    {
        if (model.IsRemoteModel || string.IsNullOrEmpty(model.ModelPath)) return false;
        string builtIn = Path.GetFullPath(ModelSettingConfig.Current.DefaultLocalModelPath)
            .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return !Path.GetFullPath(model.ModelPath).StartsWith(builtIn, StringComparison.Ordinal);
    }

    [RelayCommand]
    private async Task DeleteLocalModel(ModelRunningData model)
    {
        if (!CanDeleteLocalModel(model)) return;
        if (!await _messageService.ConfirmAsync(Loc.Text(LangKey.ModelDeleteFilesConfirm, model.ModelName, model.ModelPath)))
            return;

        try
        {
            // 跑着的先停掉：Windows 上被占用的文件删不掉
            if (model.IsRunning) App.ModelService.UnloadModel(model.ModelName);
            await Task.Run(() => LocalModelDeleter.Delete(model.ModelPath));
            ModelRuntimeSettingConfig.Current.SetOverrides(model.ModelName, null);
            ModelRuntimeSettingConfig.Current.Save();
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesDone, model.ModelName),
                severity: MessageSeverity.Success);
        }
        catch (Exception e)
        {
            Log.Warning($"Delete model files failed: {model.ModelPath}, {e.Message}");
            _messageService.ShowNotification(Loc.Text(LangKey.ModelDeleteFilesFailed, e.Message),
                severity: MessageSeverity.Error);
        }

        LoadModels();
    }

    [RelayCommand]
    private void SetFavoriteModel(string? name)
    {
        if (name == null) return;
        bool isRemove = ModelSettingConfig.Current.IsFavorite(name);
        ModelSettingConfig.Current.ToggleFavorite(name);
        ModelSettingConfig.Current.Save();

        _messageService.ShowNotification(isRemove
            ? Loc.Text(LangKey.FavoriteRemoteModelDelTips, name)
            : Loc.Text(LangKey.FavoriteRemoteModelSetTips, name));
    }

    partial void OnModelPathChanged(string? value)
    {
        LoadModels();
        // 切页时的对齐不是用户改的路径,不该弹提示
        if (!_isSyncingModelPath) _messageService.ShowNotification(Loc.Text(LangKey.ModelListUpdated));
    }

    // 原先是 async void：catch 之外再漏一个异常就是进程级崩溃。改成即发即忘，忙标志与日志交给作用域
    private void LoadModels()
    {
        _ = AsyncCommandScope.RunAsync(v => IsBusy = v, App.ModelService.LoadModelList);
    }
}
