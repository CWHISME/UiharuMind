using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Core.SimpleLog;
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
                // 存档类型与运行时类型不匹配：按注册表解析的目标类型重读一遍（source-gen，类型图在 SaveJsonContext）。
                // 原反射路径按运行时类型序列化；四家配置均无子类专属新增属性，因此 base 类型图序列化与其等价——
                // 该碰巧由 RemoteModelConfigRepairShapeTests 钉成显式断言。
                if (SaveUtility.LoadFromString(SaveUtility.SaveToString<BaseRemoteModelConfig>(config.Config),
                        RemoteModelConfigRegistry.ResolveConfigType(config.Config.ConfigType), SaveUtility.SaveJsonOptions) is
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
        string endpoint = model.ModelPath + (model.Port > 0 ? ":" + model.Port : "");
        string apiKey = model is RemoteModelInfo remoteModel ? remoteModel.ApiKey : "";
        // 亲和三层在这里合流：公共头默认发、协议头由 handler 按线补、供应商只补自己独有的那几条。
        // wire 格式只由模型声明这一处给出，地址归一也交给 handler（同一个格式决定补哪个后缀）——
        // 早先是这里先归一一次、handler 再按地址猜一次，两处得手工对齐，对不上就是
        // 「模型声明走 messages、请求却打到 chat/completions」那种看不懂的 404
        var handler = new WireHttpHandler(endpoint, CreateReasoningFieldObserver(model),
            model.SessionAffinityHeaders, model.ApiFormat, model.SendsSessionAffinity);
        // SDK 默认重试对限流太急(3 次 / 6 秒内打完),免费档模型的共享配额窗口远不止这么短;
        // 同一把密钥上并行的请求经发送闸排队,不再各自退避互撞
        return ChatClientFactory.Create(handler, model, model.ModelId, apiKey, new RateLimitAwareRetryPolicy(),
            SendGate.For(endpoint, apiKey, model.ModelId));
    }

    /// <summary>
    /// 响应观测到思考字段原名(reasoning/reasoning_text)时的回调:落盘到该模型配置(按 ModelId 分开记),
    /// 下次请求按同名回传。值变了才写,避免流式思考逐帧触发配置存档。
    /// </summary>
    private static Action<string>? CreateReasoningFieldObserver(ILlmModel model)
    {
        if (model is not RemoteModelInfo remote) return null;
        return CreateThinkingFieldObserver(remote.Config, () => RemoteModelSettingConfig.Current.Save());
    }

    /// <summary>
    /// 思考字段观测的落盘闭包（独立出来以便测试注入 save）。
    /// 比较+赋值+Save 锁在 <paramref name="config"/> 上:<see cref="RemoteModelSettingConfig.Current"/>
    /// 里同一个模型自始至终是同一个 config 实例,跨请求并发流共享这把锁,避免"双线程同值首写"重复落盘。
    /// Save 失败只记日志,不打断响应读取(内存值已更新,失败只在重启后丢)。
    /// </summary>
    internal static Action<string> CreateThinkingFieldObserver(BaseRemoteModelConfig config, Action save)
    {
        return field =>
        {
            lock (config)
            {
                if (config.ObservedThinkingFields.TryGetValue(config.ModelId, out string? current) && current == field) return;
                config.ObservedThinkingFields[config.ModelId] = field;
                try
                {
                    save();
                }
                catch (Exception e)
                {
                    Log.Warning($"could not persist observed thinking field '{field}': {e.Message}");
                }
            }
        };
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
