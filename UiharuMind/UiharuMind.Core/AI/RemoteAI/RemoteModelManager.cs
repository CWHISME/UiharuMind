using Microsoft.SemanticKernel;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Interfaces;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.ServerKernal;

namespace UiharuMind.Core.RemoteOpenAI;

public class RemoteModelManager : ServerKernalBase<RemoteModelManager, RemoteModelSettingConfig>,
    ILlmRuntime
{
    public readonly Dictionary<string, ModelRunningData> RemoteListModels = new Dictionary<string, ModelRunningData>();

    public RemoteModelManager()
    {
        foreach (var info in Config.ModelInfos)
        {
            var config = info.Value;
            if (config.Config is { ConfigType: not null } &&
                config.Config.GetType().Name != config.Config.ConfigType)
            {
                if (SaveUtility.LoadFromString(SaveUtility.SaveToString(config.Config),
                        GetType().Assembly
                            .GetType(typeof(BaseRemoteModelConfig).Namespace + "." + config.Config.ConfigType)) is
                    BaseRemoteModelConfig correct) config.Config = correct;
            }

            RemoteListModels[info.Value.ModelName] = new ModelRunningData(this, config);
        }
    }

    public async Task Run(ILlmModel model, Action<float>? onLoading = null, Action<Kernel>? onLoadOver = null,
        CancellationToken token = default)
    {
        onLoadOver?.Invoke(CreateKernel(model));
        while (!token.IsCancellationRequested)
        {
            try
            {
                // 使用 Task.Delay 无限期地等待，直到 CancellationToken 被触发
                await Task.Delay(Timeout.Infinite, token);
            }
            catch (OperationCanceledException)
            {
            }
        }
    }

    private Kernel CreateKernel(ILlmModel model)
    {
        var kernelBuilder = Kernel.CreateBuilder()
            .AddOpenAIChatCompletion(model.ModelId, model.ApiKey,
                httpClient: new HttpClient(
                    new SKernelHttpDelegatingHandler(model.ModelPath + (model.Port > 0 ? (":" + model.Port) : ""))));
        return kernelBuilder.Build();
    }

    public void AddRemoteModel(RemoteModelInfo model)
    {
        Config.ModelInfos[model.ModelName] = model;
        if (RemoteListModels.TryGetValue(model.ModelName, out var data))
            data.ForceUpdateModelInfo(model);
        else RemoteListModels[model.ModelName] = new ModelRunningData(this, model);
        SaveConfig();
    }

    public void DeleteRemoteModel(string modelName)
    {
        Config.ModelInfos.Remove(modelName);
        RemoteListModels.Remove(modelName);
        SaveConfig();
    }
}