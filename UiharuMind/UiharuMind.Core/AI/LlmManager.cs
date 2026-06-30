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

using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.LocalAI;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;
using UiharuMind.Core.LLamaCpp;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI;

/// <summary>
/// 汇总管理远程、本地语言模型
/// </summary>
public class LlmManager : Singleton<LlmManager>, IInitialize
{
    /// <summary>
    /// 本地模型运行管理
    /// </summary>
    public RuntimeEngineManager RuntimeEngineManager { get; } = new RuntimeEngineManager();

    /// <summary>
    /// 远程模型
    /// </summary>
    public RemoteModelManager RemoteModelManager { get; } = new RemoteModelManager();

    public LLamaCppServerKernal LLamaCppServer => RuntimeEngineManager.LLamaCppServer;

    /// <summary>
    /// 当前运行(/选择)的模型
    /// </summary>
    public ModelRunningData? CurrentRunningModel
    {
        get => _curModelRunningData;
        set => SetCurrentRunningModel(value);
    }

    /// <summary>
    /// 当前模型列表的字典缓存
    /// </summary>
    public Dictionary<string, ModelRunningData> CacheModelDictionary => _chacheModels;

    private ModelRunningData? _curModelRunningData;

    /// <summary>
    /// 所有模型列表
    /// </summary>
    private List<ModelRunningData> _modelList = new List<ModelRunningData>();

    /// <summary>
    ///模型列表缓存
    /// </summary>
    private Dictionary<string, ModelRunningData> _chacheModels = new Dictionary<string, ModelRunningData>();

    private List<ModelRunningData> _remoteModelList = new List<ModelRunningData>();

    //======================callbacks======================

    /// <summary>
    /// 当前运行的模型改变回调
    /// </summary>
    public event Action<ModelRunningData?>? OnCurrentModelChanged;

    // /// <summary>
    // /// 当任何模型的状态改变时回调
    // /// </summary>
    // public event Action<ModelRunningData?>? OnAnyModelStateChanged;

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
        // if (UiharuCoreManager.Instance.IsWindows) SetupTestWin();
        // else SetupTest();
    }

    /// <summary>
    /// 重新加载模型列表
    /// </summary>
    /// <returns></returns>
    public async Task<List<ModelRunningData>> ReloadModelList()
    {
        _modelList.Clear();
        _chacheModels.Clear();

        // 加载远程模型
        foreach (var model in RemoteModelManager.RemoteListModels)
        {
            _chacheModels.Add(model.Key, model.Value);
            _modelList.Add(model.Value);
        }

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
        if (string.IsNullOrEmpty(modelName))
            modelName = GetPreferredModelName(false);
        if (string.IsNullOrEmpty(modelName)) return;
        if (CurrentRunningModel != null && CurrentRunningModel.ModelName == modelName &&
            CurrentRunningModel.IsRunning) return;
        if (_chacheModels.TryGetValue(modelName, out var runningInfo))
        {
            CurrentRunningModel?.StopRunning();
            SetCurrentRunningModel(runningInfo, false);
            // 通知当前运行的模型开始加载
            OnCurrentModelStartLoading?.Invoke();
            bool loadedCallbackInvoked = false;
            try
            {
                await CurrentRunningModel.StartLoad(OnCurrentModelLoading, () =>
                {
                    loadedCallbackInvoked = true;
                    OnCurrentModelLoaded?.Invoke();
                });
            }
            catch (Exception e)
            {
                Log.Error(e.Message);
                SetCurrentRunningModel(null);
            }
            finally
            {
                if (!loadedCallbackInvoked) OnCurrentModelLoaded?.Invoke();
            }
            // 通知当前运行的模型改变
            // if (runningInfo == CurrentRunningModel) CurrentRunningModel = null;
            // 通知有任意模型状态改变
            // if (false == CurrentRunningModel.IsRunning) OnAnyModelStateChanged?.Invoke(runningInfo);
        }
        else Log.Error($"load model error， {modelName} not found in cache.");
    }

    public string? GetPreferredModelName(bool isVision)
    {
        if (CurrentRunningModel != null && IsModelCompatible(CurrentRunningModel, isVision))
            return CurrentRunningModel.ModelName;

        string favoriteModel = ModelSettingConfig.Current.FavoriteModel;
        if (!string.IsNullOrEmpty(favoriteModel) &&
            _chacheModels.TryGetValue(favoriteModel, out var favorite) &&
            IsModelCompatible(favorite, isVision))
            return favorite.ModelName;

        return _modelList.FirstOrDefault(model => IsModelCompatible(model, isVision))?.ModelName;
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

        if (CurrentRunningModel == runningInfo) CurrentRunningModel = null;
    }

    /// <summary>
    /// 检查当前是否存在允许中模型，如果没有运行中模型，优先取远程模型
    /// </summary>
    /// <returns></returns>
    public bool TryCheckModelRunning(bool isVision)
    {
        return TryCheckModelRunning(isVision, ref _curModelRunningData);
    }

    /// <summary>
    /// 检查当前是否存在允许中模型，如果没有运行中模型，优先取远程模型
    /// </summary>
    /// <returns></returns>
    public bool TryCheckModelRunning(bool isVision, ref ModelRunningData? modelRunning)
    {
        if (modelRunning == null || isVision && !modelRunning.IsVisionModel)
        {
            string? modelName = GetPreferredModelName(isVision);
            if (!string.IsNullOrWhiteSpace(modelName) &&
                _chacheModels.TryGetValue(modelName, out var preferredModel))
                modelRunning = preferredModel;
            OnCurrentModelChanged?.Invoke(modelRunning);
        }

        if (modelRunning is not { IsRunning: true })
        {
            if (modelRunning?.IsRemoteModel != true)
            {
                return false;
            }

            if (modelRunning.ChatClient == null)
            {
                _ = modelRunning.StartLoad(null, null);
            }
        }

        return true;
    }

    private void SetCurrentRunningModel(ModelRunningData? value, bool stopPrevious = true)
    {
        if (value == _curModelRunningData) return;
        if (stopPrevious && _curModelRunningData?.IsRunning == true) _curModelRunningData.StopRunning();
        _curModelRunningData = value;
        OnCurrentModelChanged?.Invoke(value);
    }

    private static bool IsModelCompatible(ModelRunningData model, bool isVision)
    {
        return isVision ? model.IsVisionModel : !model.IsVisionModel;
    }

    //======================test=========================
    // private void SetupTestWin()
    // {
    //     LLamaCppServer.Config.LLamaCppPath = "D:\\Solfware\\AI\\llama-b3772-bin-win-vulkan-x64";
    //     if (!Directory.Exists(LLamaCppServer.Config.LocalModelPath))
    //         LLamaCppServer.Config.LocalModelPath = "D:\\Solfware\\AI\\LLM_Models";
    // }
    //
    // private void SetupTest()
    // {
    //     LLamaCppServer.Config.LLamaCppPath =
    //         "/Users/dragonplus/Documents/Studys/llamacpp/llama-b3828-bin-macos-arm64/bin";
    //     LLamaCppServer.Config.LocalModelPath = "/Users/dragonplus/Documents/Studys/LLMModels";
    // }
}
