using UiharuMind.Core.Core.Interfaces;

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
    /// 是否处于运行中
    /// </summary>
    public bool IsRunning => _cts?.IsCancellationRequested ?? false;

    public ModelRunningData(ILLMModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    public void ForceUpdateModelInfo(ILLMModel modelInfo)
    {
        _modelInfo = modelInfo;
    }

    public async Task Load(int port)
    {
        if (_cts != null) return;
        await UiharuCoreManager.Instance.LLamaCppServer.StartServer(_modelInfo.ModelPath, port, OnInitLoad);
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
}