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
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Shared.Services;

/// <summary>
/// 模型管理, 管理模型列表和当前运行中的模型，表现层必须从这里调用
/// 表现层不要直接调用 LlmManager
/// </summary>
public partial class ModelService : ObservableObject
{
    public ObservableCollection<ModelRunningData> ModelSources { get; set; } =
        new ObservableCollection<ModelRunningData>();

    //选择本地模型会有风险运行提示，取消后还原
    private ModelRunningData? _modelSelectionOverride;

    public ModelRunningData? CurModelRunningData
    {
        get => _modelSelectionOverride ?? LlmManager.Instance.CurrentRunningModel;
        set
        {
            if (value == null || value == CurModelRunningData) return;
            _modelSelectionOverride = value;
            Refresh();
            _ = LoadModelWithRiskConfirmationAsync(value);
        }
    }

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private float _loadingProgress;

    /// <summary>
    /// 当前是否有运行中的模型
    /// </summary>
    public bool CurIsRunning => CurModelRunningData?.IsRunning ?? false;

    /// <summary>
    /// 当前处于运行中的模型数量
    /// </summary>
    public int CurRunningCount => ModelSources.Count(x => x.IsRunning);

    public ModelService()
    {
        LlmManager.Instance.OnCurrentModelChanged += OnCurrentModelStateChanged;
        // LlmManager.Instance.OnAnyModelStateChanged += OnAnyModelStateChanged;
        LlmManager.Instance.OnCurrentModelStartLoading += OnCurrentModelStartLoading;
        LlmManager.Instance.OnCurrentModelLoading += OnCurrentModelLoading;
        LlmManager.Instance.OnCurrentModelLoaded += OnCurrentModelLoaded;
        ModelSettingConfig.Current.PropertyChanged += OnFavoriteModelConfigChanged;
        LoadModelListAsync();
    }

    private void OnCurrentModelStartLoading()
    {
        LoadingProgress = 0;
        IsLoading = true;
        Refresh();
    }

    private void OnCurrentModelLoading(float obj)
    {
        LoadingProgress = obj;
    }

    private void OnCurrentModelLoaded()
    {
        IsLoading = false;
        Refresh();
    }

    [RelayCommand]
    public void EjectCurrentModel()
    {
        // CurModelRunningData?.StopRunning();
        // CurModelRunningData = FindIsRunningModel();
        _modelSelectionOverride = null;
        LlmManager.Instance.UnloadModel();
    }

    public async Task<bool> LoadModelWithRiskConfirmationAsync(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return false;
        ModelRunningData? model = ModelSources.FirstOrDefault(x => x.ModelName == modelName);
        if (model != null) return await LoadModelWithRiskConfirmationAsync(model);
        return false;
    }

    private async Task<bool> LoadModelWithRiskConfirmationAsync(ModelRunningData model)
    {
        if (!await ConfirmLocalLoadRiskAsync(model.ModelName, model.IsRemoteModel).ConfigureAwait(false))
        {
            ResetModelSelection();
            return false;
        }

        await LlmManager.Instance.LoadModel(model.ModelName).ConfigureAwait(false);
        ResetModelSelection();
        return true;
    }

    private void ResetModelSelection()
    {
        // ComboBox 已经把目标值写进 UI 选择状态；取消加载后需要显式让绑定回读当前真实运行模型。
        _modelSelectionOverride = null;
        Refresh();
    }

    private static async Task<bool> ConfirmLocalLoadRiskAsync(string modelName, bool isRemoteModel)
    {
        if (isRemoteModel) return true;

        RuntimeLoadRisk risk = await Task.Run(() => LlmManager.Instance.AnalyzeLoadRisk(modelName));
        if (!risk.RequiresConfirmation) return true;

        string message = string.Format(
            L("ModelRuntimeLoadRiskConfirmFormat"),
            modelName,
            FormatRiskLevel(risk.Level),
            FormatBytes(risk.EstimatedTotalBytes),
            string.IsNullOrWhiteSpace(risk.Reason) ? "-" : risk.Reason);

        if (risk.Warnings.Count > 0)
            message += Environment.NewLine + string.Join(Environment.NewLine, risk.Warnings.Select(x => $"- {x}"));
        message += Environment.NewLine + L("ModelRuntimeLoadRiskNativeCrashHint");

        IMessageService messageService = App.Services.GetRequiredService<IMessageService>();
        return await messageService.ConfirmAsync(message, L("ModelRuntimeLoadRiskConfirmTitle"));
    }

    private static string FormatRiskLevel(RuntimeLoadRiskLevel level)
    {
        return level switch
        {
            RuntimeLoadRiskLevel.Danger => L("ModelRuntimeRiskDanger"),
            RuntimeLoadRiskLevel.Warning => L("ModelRuntimeRiskWarning"),
            RuntimeLoadRiskLevel.Unknown => L("ModelRuntimeRiskUnknown"),
            _ => L("ModelRuntimeRiskLow")
        };
    }

    private static string FormatBytes(long bytes)
    {
        return bytes <= 0 ? "-" : Core.Core.Utils.GameUtils.FormatBytes(bytes);
    }

    public async void LoadModelListAsync()
    {
        await LoadModelList();
    }

    public async Task LoadModelList()
    {
        var list = await LlmManager.Instance.ReloadModelList().ConfigureAwait(false);

        //清理旧的
        List<ModelRunningData> toDel = new List<ModelRunningData>();
        foreach (var oldItem in ModelSources)
        {
            if (LlmManager.Instance.CacheModelDictionary.TryGetValue(oldItem.ModelName, out var model) &&
                model == oldItem) continue;
            toDel.Add(oldItem);
        }

        foreach (var model in toDel)
        {
            ModelSources.Remove(model);
        }

        //添加新的
        // ModelSources.Clear();
        foreach (var model in list)
        {
            if (ModelSources.Contains(model)) continue;
            if (model.IsRemoteModel) ModelSources.Insert(0, model);
            else ModelSources.Add(model);
        }

        Refresh();
    }

    private void OnFavoriteModelConfigChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ModelSettingConfig.FavoriteModels)) return;
        foreach (var item in ModelSources)
        {
            item.NotifyFavoriteChanged();
        }
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CurIsRunning));
        OnPropertyChanged(nameof(CurRunningCount));
        OnPropertyChanged(nameof(CurModelRunningData));
        // OnPropertyChanged(nameof(ModelSources));
    }

    // ======= event =======

    private void OnCurrentModelStateChanged(ModelRunningData? model)
    {
        Refresh();
    }

    private void OnAnyModelStateChanged(ModelRunningData? model)
    {
        Refresh();
    }

    private static string L(string key)
    {
        return LocalizationManager.Instance.GetString(key);
    }
}