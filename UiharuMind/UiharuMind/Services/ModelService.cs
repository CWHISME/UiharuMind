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
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Services;

/// <summary>
/// 模型管理, 管理模型列表和当前运行中的模型，表现层必须从这里调用
/// 表现层不要直接调用 LlmManager
/// </summary>
public partial class ModelService : ObservableObject
{
    public ObservableCollection<ModelRunningData> ModelSources { get; set; } =
        new ObservableCollection<ModelRunningData>();

    public ModelRunningData? CurModelRunningData
    {
        get => LlmManager.Instance.CurrentRunningModel;
        set => LlmManager.Instance.CurrentRunningModel = value;
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
        LlmManager.Instance.UnloadModel();
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
}