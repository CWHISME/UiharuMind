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
        // HttpClient.Timeout 默认就是 100s(管到响应头/首字节),SDK 自带客户端反而设成了 Infinite。
        // 流式长思考会撞它,一并关掉,裁决交给上层 CancellationToken
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var options = new OpenAIClientOptions
        {
            Transport = new HttpClientPipelineTransport(httpClient),
            // SDK 默认重试对限流太急(3 次 / 6 秒内打完),免费档模型的共享配额窗口远不止这么短
            RetryPolicy = new RateLimitAwareRetryPolicy(),
            // 流式响应的读闸默认 100s:思考期 chunk 间隔一长就被 ReadTimeoutStream 掐断,
            // 且那异常在 HTTP 200 之后冒出,OpenAICompatibleHttpHandler 看不到,只会被上层当用户取消吞掉。
            // 读卡的裁决完全交给上层 CancellationToken(用户停止按钮),这里不设静态时限
            NetworkTimeout = Timeout.InfiniteTimeSpan,
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
