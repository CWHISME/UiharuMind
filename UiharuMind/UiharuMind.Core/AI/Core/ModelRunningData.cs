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

using System.Diagnostics.CodeAnalysis;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI.Core;

public class ModelRunningData
{
    private ILlmRuntime _runtime;
    private ILlmModel _modelInfo;

    private Kernel? _kernel;

    // private ChatThread? _chatThread;
    private CancellationTokenSource? _cts;

    public ILlmModel ModelInfo => _modelInfo;

    /// <summary>
    /// 请先检测模型是否运行
    /// </summary>
    public Kernel? Kernel => _kernel;

    /// <summary>
    /// 当前运行的模型名称
    /// </summary>
    public string ModelName => _modelInfo.ModelName;

    /// <summary>
    /// 是否是远程模型
    /// </summary>
    public bool IsRemoteModel => _modelInfo is RemoteModelInfo;

    /// <summary>
    /// 是否是视觉模型
    /// </summary>
    public bool IsVisionModel => _modelInfo.IsVision;

    /// <summary>
    /// 模型路径
    /// </summary>
    public string ModelPath => _modelInfo.ModelPath;

    /// <summary>
    /// 是否处于运行中
    /// </summary>
    public bool IsRunning => _isLoaded && _kernel != null && !((_cts?.IsCancellationRequested) ?? true);

    /// <summary>
    /// 0~1,1表示加载完成 100%
    /// </summary>
    public float LoadingPercent { get; private set; } = 0;

    private bool _isLoaded = false;
    // private int _loadingCount = 0;
    // private Action<float>? _onLoading;
    // private Action? _onLoaded;

    public ModelRunningData(ILlmRuntime runtime, ILlmModel modelInfo)
    {
        _runtime = runtime;
        _modelInfo = modelInfo;
    }

    public void ForceUpdateModelInfo(ILlmModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    /// <summary>
    /// 加载模型，如果模型已经处于运行中，则不进行任何操作
    /// 远程模型将立即加载完成
    /// </summary>
    /// <param name="onLoading"></param>
    /// <param name="onLoaded"></param>
    public async Task StartLoad(Action<float>? onLoading, Action? onLoaded)
    {
        if (_cts != null) return;
        _isLoaded = false;
        // _loadingCount = 0;
        LoadingPercent = 0;
        // _onLoading = onLoading;
        // _onLoaded = onLoaded;
        _cts = new CancellationTokenSource();
        await _runtime.Run(_modelInfo, (x) =>
        {
            LoadingPercent = x;
            onLoading?.Invoke(x);
        }, (kernal) =>
        {
            _kernel = kernal;
            _isLoaded = true;
            onLoaded?.Invoke();
        }, _cts.Token);
        // await LlmManager.Instance.RuntimeEngineManager.LLamaCppServer.StartServer(_modelInfo.ModelPath, Port,
        //     OnInitLoad,
        //     OnMessageUpdate);
        if (!IsRemoteModel) StopRunning();
    }

    /// <summary>
    /// 如果处于运行中，则停止运行
    /// </summary>
    public void StopRunning()
    {
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
        _cts = null;
    }

    //=========================================================================================================

    // public IAsyncEnumerable<string> SendMessageStreamingAsync(ChatHistory chatHistory, CancellationToken token)
    // {
    //     // if (!IsRunning)
    //     // {
    //     //     onMessageReceived.Invoke($"{ModelName} is not running");
    //     //     yield break;
    //     // }
    //     if (_kernel == null) return new AsyncEnumerableWithMessage("Model is not loaded");
    //
    //     _chatThread ??= new ChatThread(_kernel);
    //     return _chatThread.SendMessageStreamingAsync(chatHistory, token);
    // }

    // public void SendMessageStreaming(ChatHistory chatHistory,
    //     Action<DateTimeOffset>? onMessageStart,
    //     Action<ChatStreamingMessageInfo> onMessageReceived,
    //     Action<ChatStreamingMessageInfo> onMessageStopped,
    //     CancellationToken token)
    // {
    //     if (_kernel == null)
    //     {
    //         Log.Error("Model is not loaded");
    //         return;
    //     }
    //
    //     _chatThread ??= new ChatThread(_kernel);
    //     _chatThread.SendMessageStreaming(chatHistory, onMessageStart, onMessageReceived, onMessageStopped, token);
    // }


    //For agent
    // public async Task InvokeAgentStreamingAsync(ChatSession chatSession)
    // {
    // }

    // private void OnInitLoad(CancellationTokenSource cts)
    // {
    //     _cts = cts;
    // }
}