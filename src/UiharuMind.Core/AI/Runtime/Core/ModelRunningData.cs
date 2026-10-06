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

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.RemoteOpenAI;

namespace UiharuMind.Core.AI.Core;

public class ModelRunningData : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public bool IsFavorite => ModelSettingConfig.Current.IsFavorite(ModelName);
    private ILlmModel _modelInfo;

    private IChatClient? _chatClient;
    private int _runtimeContextSize; //本地模型实际加载的上下文大小,0 表示未知(远程模型不用它)

    // private ChatThread? _chatThread;
    private CancellationTokenSource? _cts;

    public ILlmModel ModelInfo => _modelInfo;

    /// <summary>
    /// 请先检测模型是否运行
    /// </summary>
    public IChatClient? ChatClient => _chatClient;

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
    /// 是否支持工具调用，由跑它的引擎声明（<see cref="RuntimeCapability.ToolCalling"/>）。
    /// 没经过运行时服务登记的（如远程模型设置里的那份列表）按远程算支持
    /// </summary>
    public bool SupportsToolCalling => _supportsToolCalling ?? IsRemoteModel;

    /// <summary>
    /// 本模型的上下文窗口(token 数)。远程按配置/预设表解析，本地按实际加载值；
    /// 是历史压缩预算与界面占用显示的共同依据。
    /// </summary>
    public int ContextLength => _modelInfo is RemoteModelInfo remote
        ? ModelContextResolver.ResolveRemote(remote)
        : ModelContextResolver.ResolveLocal(_runtimeContextSize);

    /// <summary>
    /// 模型路径
    /// </summary>
    public string ModelPath => _modelInfo.ModelPath;

    public string ModelMetadataSummary => _modelInfo is GGufModelInfo ggufModelInfo
        ? ggufModelInfo.MetadataSummary
        : "";

    /// <summary>
    /// 是否处于运行中
    /// </summary>
    public bool IsRunning => _isLoaded && _chatClient != null && !((_cts?.IsCancellationRequested) ?? true);

    /// <summary>
    /// 是否正在加载中（已开始、既未就绪也不算失败）。重复拉起会重置进度并造成双跑，
    /// 自动拉起前必须先看它，而不是只看 <see cref="IsRunning"/>
    /// </summary>
    public bool IsLoading => !_isLoaded && _cts != null;

    /// <summary>
    /// 0~1,1表示加载完成 100%
    /// </summary>
    public float LoadingPercent { get; private set; } = 0;

    private bool _isLoaded = false;
    private bool? _supportsToolCalling;

    /// <summary>
    /// 最近一次加载失败或运行中崩溃的原因；开始加载时清空
    /// </summary>
    public Exception? LastError { get; private set; }
    // private int _loadingCount = 0;
    // private Action<float>? _onLoading;
    // private Action? _onLoaded;

    public ModelRunningData(ILlmModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    public void NotifyFavoriteChanged()
    {
        OnPropertyChanged(nameof(IsFavorite));
    }

    /// <summary>
    /// 通知运行、加载状态变了（状态本身在后台线程改，由界面层在 UI 线程上调这个）
    /// </summary>
    public void NotifyStateChanged()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(IsLoading));
    }

    public void ForceUpdateModelInfo(ILlmModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    /// <summary>
    /// 记下将来跑它的引擎能做什么
    /// </summary>
    /// <param name="backend">引擎，null 表示没有能跑它的</param>
    public void ApplyBackend(IModelRuntimeBackend? backend)
    {
        _supportsToolCalling = backend?.Capabilities.Contains(RuntimeCapability.ToolCalling) == true;
    }

    public CancellationToken BeginLoading()
    {
        _isLoaded = false;
        _chatClient = null;
        LoadingPercent = 0;
        LastError = null;
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    public void UpdateLoading(float loadingPercent)
    {
        LoadingPercent = loadingPercent;
    }

    public void CompleteLoading(IChatClient chatClient, int runtimeContextSize = 0)
    {
        _chatClient = chatClient;
        _runtimeContextSize = runtimeContextSize;
        _isLoaded = true;
        LoadingPercent = 1;
    }

    public void FailLoading(Exception? error = null)
    {
        LastError = error;
        if (!_isLoaded)
        {
            _chatClient = null;
            LoadingPercent = 0;
        }

        _cts = null;
    }

    /// <summary>
    /// 跑着跑着出错（如本地进程崩溃）：记下原因并停下
    /// </summary>
    /// <param name="error">原因</param>
    public void FailRunning(Exception error)
    {
        StopRunning();
        LastError = error;
    }

    /// <summary>
    /// 如果处于运行中，则停止运行
    /// </summary>
    public void StopRunning()
    {
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
        if (_chatClient is IDisposable disposable) disposable.Dispose();
        _chatClient = null;
        _isLoaded = false;
        LoadingPercent = 0;
        _cts = null;
    }

}
