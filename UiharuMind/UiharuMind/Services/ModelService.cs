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
    public List<ModelRunningData> ModelSources { get; set; } = new List<ModelRunningData>();

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
        LlmManager.Instance.OnAnyModelStateChanged += OnAnyModelStateChanged;
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
        CurModelRunningData?.StopRunning();
        // CurModelRunningData = FindIsRunningModel();
    }

    public async void LoadModelListAsync()
    {
        await LoadModelList();
    }

    public async Task LoadModelList()
    {
        var list = await LlmManager.Instance.ReloadModelList().ConfigureAwait(false);
        ModelSources.Clear();
        foreach (var model in list)
        {
            ModelSources.Add(model);
        }

        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(CurIsRunning));
        OnPropertyChanged(nameof(CurRunningCount));
        OnPropertyChanged(nameof(CurModelRunningData));
        OnPropertyChanged(nameof(ModelSources));
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