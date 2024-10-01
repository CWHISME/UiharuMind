using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.LocalAI;
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
    /// 本地模型运行管理
    /// </summary>
    public RuntimeEngineManager RuntimeEngineManager { get; private set; } = new RuntimeEngineManager();

    public LLamaCppServerKernal LLamaCppServer => RuntimeEngineManager.LLamaCppServer;

    /// <summary>
    /// 当前运行(/选择)的模型
    /// </summary>
    public ModelRunningData? CurrentRunningModel
    {
        get => _curModelRunningData;
        set
        {
            if (value == _curModelRunningData) return;
            if (_curModelRunningData?.IsRunning == true) _curModelRunningData.StopRunning();
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
    public event Action<ModelRunningData?>? OnCurrentModelChanged;

    /// <summary>
    /// 当任何模型的状态改变时回调
    /// </summary>
    public event Action<ModelRunningData?>? OnAnyModelStateChanged;

    /// <summary>
    /// 当前模型开始加载
    /// </summary>
    public event Action? OnCurrentModelStartLoading;

    /// <summary>
    /// 当的模型加载进度回调
    /// </summary>
    public event Action<float>? OnCurrentModelLoading;

    /// <summary>
    /// 当前运行的模型加载完成回调
    /// </summary>
    public event Action? OnCurrentModelLoaded;

    public void OnInitialize()
    {
        if (UiharuCoreManager.Instance.IsWindows) SetupTestWin();
        else SetupTest();
    }

    /// <summary>
    /// 重新加载模型列表
    /// </summary>
    /// <returns></returns>
    public async Task<List<ModelRunningData>> ReloadModelList()
    {
        _modelList.Clear();
        _chacheModels.Clear();
        var modelList = await Task.Run(RuntimeEngineManager.GetModelList).ConfigureAwait(false);
        foreach (var model in modelList)
        {
            _chacheModels.Add(model.Key, model.Value);
            _modelList.Add(model.Value);
        }

        return _modelList;
    }

    /// <summary>
    /// 获取模型列表，请确保已经加载完毕
    /// </summary>
    public List<ModelRunningData> GetModelList()
    {
        return _modelList;
    }

    /// <summary>
    /// 运行当前选择的模型
    /// </summary>
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
            // 通知当前运行的模型开始加载
            OnCurrentModelStartLoading?.Invoke();
            await CurrentRunningModel.StartLoad(OnCurrentModelLoading, OnCurrentModelLoaded);
            // 通知当前运行的模型改变
            if (runningInfo == CurrentRunningModel) CurrentRunningModel = null;
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
        LLamaCppServer.Config.LLamaCppPath = "D:\\Solfware\\AI\\llama-b3772-bin-win-vulkan-x64";
        if (!Directory.Exists(LLamaCppServer.Config.LocalModelPath))
            LLamaCppServer.Config.LocalModelPath = "D:\\Solfware\\AI\\LLM_Models";
    }

    private void SetupTest()
    {
        LLamaCppServer.Config.LLamaCppPath =
            "/Users/dragonplus/Documents/Studys/llamacpp/llama-b3828-bin-macos-arm64/bin";
        LLamaCppServer.Config.LocalModelPath = "/Users/dragonplus/Documents/Studys/LLMModels";
    }
}