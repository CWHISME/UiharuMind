using UiharuMind.Core.Core.Interfaces;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.LLamaCpp.Data;

public class ModelRunningData
{
    private ILLMModel _modelInfo;
    private CancellationTokenSource? _cts;

    public ILLMModel ModelInfo => _modelInfo;

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
    public bool IsRunning => _cts?.IsCancellationRequested ?? false;

    /// <summary>
    /// 0~100,100表示加载完成 100%
    /// </summary>
    public int LoadingCount { get; private set; } = 0;

    private Action<float> _onLoading;

    public ModelRunningData(ILLMModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    public void ForceUpdateModelInfo(ILLMModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    public async Task Load(int port, Action<float> onLoading)
    {
        if (_cts != null) return;
        _onLoading = onLoading;
        await UiharuCoreManager.Instance.LLamaCppServer.StartServer(_modelInfo.ModelPath, port, OnInitLoad,
            OnMessageUpdate);
    }

    /// <summary>
    /// 如果处于运行中，则停止运行
    /// </summary>
    public void StopRunning()
    {
        _cts?.Cancel();
        _cts = null;
    }

    private void OnInitLoad(CancellationTokenSource cts)
    {
        _cts = cts;
    }

    private void OnMessageUpdate(string msg)
    {
        Log.Debug(msg);
        if (LoadingCount < 100 && msg.StartsWith('.')) LoadingCount++;
    }
}