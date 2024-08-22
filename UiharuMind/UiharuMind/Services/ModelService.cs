using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core;
using UiharuMind.Core.Core.Interfaces;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Services;

/// <summary>
/// 模型管理
/// </summary>
public partial class ModelService : ObservableObject
{
    public ObservableCollection<ModelRunningData> ModelSources { get; set; } =
        new ObservableCollection<ModelRunningData>();

    // [ObservableProperty] HashSet<ModelRunningData> RunningModels = new HashSet<ModelRunningData>();
    // public ObservableCollection<ModelRunningData> ModelRunningDatas { get; set; } =
    //     new ObservableCollection<ModelRunningData>();

    [ObservableProperty] private ModelRunningData? _curModelRunningData;

    /// <summary>
    /// 当前是否有运行中的模型
    /// </summary>
    public bool CurIsRunning => CurModelRunningData != null;

    /// <summary>
    /// 当前处于运行中的模型数量
    /// </summary>
    public int CurRunningCount => ModelSources.Count(x => x.IsRunning);

    // [ObservableProperty]
    private Dictionary<string, ModelRunningData> _chacheModels = new Dictionary<string, ModelRunningData>();

    public ModelService()
    {
        _ = LoadModelList();
    }

    public async Task LoadModelList()
    {
        var modelList = await UiharuCoreManager.Instance.LLamaCppServer.GetModelList().ConfigureAwait(false);

        // ModelSources.Clear();
        foreach (var model in modelList)
        {
            if (_chacheModels.TryGetValue(model.Key, out var runningData))
            {
                runningData.ForceUpdateModelInfo(model.Value);
                continue;
            }

            runningData = new ModelRunningData(model.Value);
            _chacheModels[model.Key] = runningData;
            ModelSources.Add(runningData);
        }

        //TOOD：排除失效的

        // OnPropertyChanged(nameof(ModelSources));
    }

    [RelayCommand]
    public void EjectCurrentModel()
    {
        CurModelRunningData?.StopRunning();
        CurModelRunningData = FindIsRunningModel();
    }

    async partial void OnCurModelRunningDataChanged(ModelRunningData? value)
    {
        // Log.Debug("改变====>:" + value?.ModelName);
        if (value == null) CurModelRunningData = FindIsRunningModel();
        else await LoadModel(value);
    }

    /// <summary>
    /// 运行指定模型，如果模型已经处于运行中，则无操作
    /// </summary>
    /// <param name="modelInfo"></param>
    private async Task LoadModel(ModelRunningData? modelInfo)
    {
        if (modelInfo == null) return;
        if (_chacheModels.TryGetValue(modelInfo.ModelName, out var runningInfo))
        {
            CurModelRunningData = runningInfo;
            RefreshUI();
            await CurModelRunningData.Load(UiharuCoreManager.Instance.LLamaCppServer.Config.DefautPort);
        }
        else Log.Error($"不存在 {modelInfo.ModelName}");
    }

    private ModelRunningData? FindIsRunningModel()
    {
        return _chacheModels.FirstOrDefault(x => x.Value.IsRunning).Value;
    }

    private void RefreshUI()
    {
        OnPropertyChanged(nameof(CurIsRunning));
        OnPropertyChanged(nameof(CurRunningCount));
    }
}