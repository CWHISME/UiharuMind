namespace UiharuMind.Core.LLamaCpp.Data;

public class ModelRunningData
{
    private GGufModelInfo _modelInfo;

    /// <summary>
    /// 当前运行的模型名称
    /// </summary>
    public string ModelName => _modelInfo.ModelName;

    public async Task Load(GGufModelInfo modelInfo, int port)
    {
        _modelInfo = modelInfo;
        await UiharuCoreManager.Instance.LLamaCppServer.StartServer(modelInfo.ModelPath, port);
    }
}