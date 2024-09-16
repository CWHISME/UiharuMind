using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI;
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
        LoadModelListSync();
    }

    [RelayCommand]
    public void EjectCurrentModel()
    {
        CurModelRunningData?.StopRunning();
        // CurModelRunningData = FindIsRunningModel();
    }

    public async void LoadModelListSync()
    {
        await LoadModelList();
    }

    public async Task LoadModelList()
    {
        var list = await LlmManager.Instance.GetModelList();
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
    }

    // ======= event =======

    private void OnCurrentModelStateChanged(ModelRunningData? model)
    {
        OnPropertyChanged(nameof(CurIsRunning));
        OnPropertyChanged(nameof(CurModelRunningData));
    }

    private void OnAnyModelStateChanged(ModelRunningData? model)
    {
        OnPropertyChanged(nameof(CurRunningCount));
    }
}