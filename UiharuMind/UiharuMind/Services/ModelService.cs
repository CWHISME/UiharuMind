using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Services;

/// <summary>
/// 模型管理
/// </summary>
public partial class ModelService : ObservableObject
{
    public ObservableCollection<GGufModelInfo> ModelSources { get; set; } = new ObservableCollection<GGufModelInfo>();

    public ObservableCollection<ModelRunningData> ModelRunningDatas { get; set; } =
        new ObservableCollection<ModelRunningData>();

    [ObservableProperty] private GGufModelInfo _curModelRunningData;

    public ModelService()
    {
        _ = LoadModelList();
    }

    public async Task LoadModelList()
    {
        var modelList = await UiharuCoreManager.Instance.LLamaCppServer.GetModelList().ConfigureAwait(false);
        ModelSources.Clear();
        foreach (var model in modelList)
        {
            ModelSources.Add(model);
        }
    }

    /// <summary>
    /// 运行指定模型，如果模型已经处于运行中，则无操作
    /// </summary>
    /// <param name="modelInfo"></param>
    public async Task LoadModel(GGufModelInfo modelInfo)
    {
        foreach (var runningData in ModelRunningDatas)
        {
            if (runningData.ModelName == modelInfo.ModelName) return;
        }

        ModelRunningData data = new ModelRunningData();
        ModelRunningDatas.Add(data);
        await data.Load(modelInfo, UiharuCoreManager.Instance.LLamaCppServer.Config.DefautPort);
    }

    [RelayCommand]
    private void EjectCurrentModel()
    {
        Log.Debug("Eject Current Model");
    }

    partial void OnCurModelRunningDataChanged(GGufModelInfo value)
    {
        Log.Debug("改变====>:" + value);
    }
}