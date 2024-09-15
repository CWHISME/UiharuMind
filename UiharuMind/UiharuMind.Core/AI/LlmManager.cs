using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.LLamaCpp.Data;

namespace UiharuMind.Core.AI;

/// <summary>
/// 汇总管理远程、本地语言模型
/// </summary>
public class LlmManager : Singleton<LlmManager>, IInitialize
{
    /// <summary>
    /// llamacpp 服务
    /// </summary>
    public LLamaCppServerKernal LLamaCppServer { get; private set; } = new LLamaCppServerKernal();

    /// <summary>
    /// 当前运行(/选择)的模型
    /// </summary>
    public ModelRunningData? CurrentRunningModel
    {
        get => _curModelRunningData;
        set
        {
            if (value == _curModelRunningData) return;
            _curModelRunningData = value;
            OnCurrentModelChanged?.Invoke(value);
            LoadCurrentModel();
        }
    }

    private ModelRunningData? _curModelRunningData;

    /// <summary>
    /// 所有模型列表
    /// </summary>
    private List<ModelRunningData> _modelList = new List<ModelRunningData>();

    /// <summary>
    ///模型列表缓存
    /// </summary>
    private Dictionary<string, ModelRunningData> _chacheModels = new Dictionary<string, ModelRunningData>();

    //======================callbacks======================

    /// <summary>
    /// 当前运行的模型改变回调
    /// </summary>
    public Action<ModelRunningData?>? OnCurrentModelChanged;

    /// <summary>
    /// 当任何模型的状态改变时回调
    /// </summary>
    public Action<ModelRunningData?>? OnAnyModelStateChanged;

    public void OnInitialize()
    {
        if (UiharuCoreManager.Instance.IsWindows) SetupTestWin();
        else SetupTest();
    }

    /// <summary>
    /// 重新加载模型列表
    /// </summary>
    public async Task<List<ModelRunningData>> GetModelList()
    {
        _modelList.Clear();

        var modelList = await LLamaCppServer.GetModelList().ConfigureAwait(false);
        foreach (var model in modelList)
        {
            if (_chacheModels.TryGetValue(model.Key, out var runningData))
            {
                runningData.ForceUpdateModelInfo(model.Value);
                continue;
            }

            runningData = new ModelRunningData(model.Value);
            _chacheModels[model.Key] = runningData;
            _modelList.Add(runningData);
        }

        return _modelList;
    }

    public async void LoadCurrentModel()
    {
        await LoadModel(CurrentRunningModel?.ModelName);
    }

    /// <summary>
    /// 运行指定模型
    /// </summary>
    /// <param name="modelName"></param>
    public async Task LoadModel(string? modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return;
        if (CurrentRunningModel != null && CurrentRunningModel.ModelName == modelName &&
            CurrentRunningModel.IsRunning) return;
        CurrentRunningModel?.StopRunning();
        if (_chacheModels.TryGetValue(modelName, out var runningInfo))
        {
            CurrentRunningModel = runningInfo;
            await CurrentRunningModel.StartLoad(LLamaCppServer.Config.DefautPort, null);
            // 通知当前运行的模型改变
            if (runningInfo == CurrentRunningModel) OnCurrentModelChanged?.Invoke(runningInfo);
            // 通知有任意模型状态改变
            OnAnyModelStateChanged?.Invoke(runningInfo);
        }
        else Log.Error($"load model error， {modelName} not found in cache.");
    }

    /// <summary>
    /// 终止当前运行的模型
    /// </summary>
    public void UnloadModel()
    {
        if (CurrentRunningModel == null) return;
        UnloadModel(CurrentRunningModel.ModelName);
    }

    /// <summary>
    /// 终止指定模型
    /// </summary>
    /// <param name="modelName"></param>
    public void UnloadModel(string modelName)
    {
        if (string.IsNullOrEmpty(modelName)) return;
        if (_chacheModels.TryGetValue(modelName, out var runningInfo))
        {
            runningInfo.StopRunning();
        }
        else Log.Error($"unload model error， {modelName} not found in cache.");
    }


    //======================test=========================
    private void SetupTestWin()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "D:\\Solfware\\AI\\llama-b3058-bin-win-vulkan-x64";
        if (!Directory.Exists(LLamaCppServer.Config.LocalModelPath))
            LLamaCppServer.Config.LocalModelPath = "D:\\Solfware\\AI\\LLM_Models";
    }

    private void SetupTest()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "/Users/dragonplus/Documents/Studys/llamacpp/llama-b3617-bin-macos-arm64/bin";
        LLamaCppServer.Config.LocalModelPath = "/Users/dragonplus/Documents/Studys/LLMModels";
    }
}