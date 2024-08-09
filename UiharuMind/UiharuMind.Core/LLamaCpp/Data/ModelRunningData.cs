namespace UiharuMind.Core.LLamaCpp.Data;

public class ModelRunningData
{
    public async Task Load(GGufModelInfo modelInfo, int port)
    {
        await UiharuCoreManager.Instance.LLamaCppServer.StartServer(modelInfo.ModelPath, port);
    }
}