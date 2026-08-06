using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.LLM;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.RemoteOpenAI;

internal sealed class RemoteModelManager
{
    public readonly Dictionary<string, ModelRunningData> RemoteListModels = new();

    public RemoteModelManager()
    {
        foreach (var info in RemoteModelSettingConfig.Current.ModelInfos)
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

            RemoteListModels[info.Value.ModelName] = new ModelRunningData(config);
        }
    }

    public ModelRunningData? FindVisionModel()
    {
        ModelRunningData? modelRunning = null;
        foreach (var model in RemoteListModels)
        {
            if (model.Value.IsVisionModel)
            {
                modelRunning = model.Value;
                break;
            }
        }

        return modelRunning;
    }

    public Task Run(ILlmModel model, Action<float>? onLoading = null, Action<IChatClient>? onLoadOver = null,
        CancellationToken token = default)
    {
        onLoadOver?.Invoke(CreateChatClient(model));
        return Task.CompletedTask;
    }

    private IChatClient CreateChatClient(ILlmModel model)
    {
        var handler = new OpenAICompatibleHttpHandler(
            model, model.ModelPath + (model.Port > 0 ? ":" + model.Port : ""));
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(new HttpClient(handler)),
            // SDK 默认重试对限流太急(3 次 / 6 秒内打完),免费档模型的共享配额窗口远不止这么短
            RetryPolicy = new RateLimitAwareRetryPolicy(),
        };
        var client = new ChatClient(model.ModelId,
            new ApiKeyCredential(model is RemoteModelInfo remoteModel ? remoteModel.ApiKey : ""), options);
        return client.AsIChatClient();
    }

    public void AddRemoteModel(RemoteModelInfo model)
    {
        RemoteModelSettingConfig.Current.ModelInfos[model.ModelName] = model;
        if (RemoteListModels.TryGetValue(model.ModelName, out var data))
            data.ForceUpdateModelInfo(model);
        else RemoteListModels[model.ModelName] = new ModelRunningData(model);
        var list = SimpleObjectPool<List<string>>.Get();
        foreach (var info in RemoteModelSettingConfig.Current.ModelInfos)
        {
            if (info.Key != info.Value.ModelName) list.Add(info.Key);
        }

        //移除被改了名字的模型(新模型已添加，旧模型需要移除)
        foreach (var del in list)
        {
            RemoteListModels.Remove(del);
            RemoteModelSettingConfig.Current.ModelInfos.Remove(del);
        }

        list.Clear();
        SimpleObjectPool<List<string>>.Release(list);
        RemoteModelSettingConfig.Current.Save();
    }

    public void DeleteRemoteModel(string modelName)
    {
        RemoteModelSettingConfig.Current.ModelInfos.Remove(modelName);
        RemoteListModels.Remove(modelName);
        RemoteModelSettingConfig.Current.Save();
    }
}
