using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Interfaces;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.LLamaCpp.Data;

public class ModelRunningData
{
    private ILlmRuntimeEngine _runtimeEngine;
    private ILlmModel _modelInfo;
    private ChatThread? _chatThread;
    private CancellationTokenSource? _cts;

    public ILlmModel ModelInfo => _modelInfo;

    /// <summary>
    /// 当前运行的模型名称
    /// </summary>
    public string ModelName => _modelInfo.ModelName;

    /// <summary>
    /// 模型路径
    /// </summary>
    public string ModelPath => _modelInfo.ModelPath;

    /// <summary>
    /// 是否处于运行中
    /// </summary>
    public bool IsRunning => !((_cts?.IsCancellationRequested) ?? true);

    /// <summary>
    /// 0~1,1表示加载完成 100%
    /// </summary>
    public float LoadingPercent { get; private set; } = 0;

    // private int _loadingCount = 0;
    // private Action<float>? _onLoading;
    // private Action? _onLoaded;

    public ModelRunningData(ILlmRuntimeEngine runtimeEngine, ILlmModel modelInfo)
    {
        _runtimeEngine = runtimeEngine;
        _modelInfo = modelInfo;
    }

    public void ForceUpdateModelInfo(ILlmModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    /// <summary>
    /// 加载模型，如果模型已经处于运行中，则不进行任何操作
    /// </summary>
    /// <param name="onLoading"></param>
    /// <param name="onLoaded"></param>
    public async Task StartLoad(Action<float>? onLoading, Action? onLoaded)
    {
        if (_cts != null) return;
        // _loadingCount = 0;
        LoadingPercent = 0;
        // _onLoading = onLoading;
        // _onLoaded = onLoaded;
        await _runtimeEngine.Run(_modelInfo, OnInitLoad, (x) =>
        {
            LoadingPercent = x;
            onLoading?.Invoke(x);
        }, onLoaded);
        // await LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.StartServer(_modelInfo.ModelPath, Port,
        //     OnInitLoad,
        //     OnMessageUpdate);
        StopRunning();
    }

    /// <summary>
    /// 如果处于运行中，则停止运行
    /// </summary>
    public void StopRunning()
    {
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
        _cts = null;
        // EndLoadCheck();
        // _onLoaded?.Invoke();
        // _onLoaded = null;
    }

    public async void SendMessage(ChatHistory chatHistory, Action<string> onMessageReceived,
        Action<string> onMessageComplete)
    {
        if (!IsRunning)
        {
            Log.Error($"{ModelName} is not running");
            return;
        }

        if (_chatThread == null) _chatThread = new ChatThread();
        var result = await _chatThread.SendMessageStreamingAsync(chatHistory, onMessageReceived);
        onMessageComplete.Invoke(result);
    }

    private void OnInitLoad(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    // private void EndLoadCheck()
    // {
    //     if (_onLoaded != null)
    //     {
    //         _onLoaded();
    //         _onLoaded = null;
    //     }
    // }
}