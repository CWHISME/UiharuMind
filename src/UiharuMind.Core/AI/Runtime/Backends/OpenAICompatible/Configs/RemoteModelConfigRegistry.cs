using UiharuMind.Core.AI.Models;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 远程模型供应商配置的显式注册表：替代原先「扫整程序集找 <see cref="BaseRemoteModelConfig"/> 子类 +
/// <see cref="Activator.CreateInstance"/>」的反射装配（ModelContextResolver / RemoteModelEditViewData /
/// RemoteModelManager 三处共用）。类型被 typeof 直接引用、实例走显式工厂，AOT/裁剪友好；
/// 条目顺序固定，即界面供应商顺序与变体表合并顺序（先到先得）。
/// 新增供应商：在这里登记即可，三处消费自动生效。
///
/// <para>同时是「配置类型 ↔ models.dev 供应商键」的<b>唯一</b>登记处（ADR 0103）。原先这份映射由
/// 生成器写进 <c>ModelCapabilityTable.g.cs</c>，加一家供应商要改三处（这里、生成器的 PROVIDERS、
/// 生成器的 PROVIDER_TYPES）；现在只剩这里一处。</para>
/// </summary>
public static class RemoteModelConfigRegistry
{
    // 配置类型全集。前七项为供应商（顺序即界面显示顺序与变体表先到先得的合并顺序），
    // 通用兜底 RemoteModelConfig 永远放最后、不作为供应商展示。
    // ProviderKey 是该家 models.dev 的键；OpenRouter 自带目录、没有 models.dev 条目，留空。
    private static readonly (Type Type, Func<BaseRemoteModelConfig> Factory, string ProviderKey)[] Entries =
    [
        (typeof(RemoteAgnesModelConfig), static () => new RemoteAgnesModelConfig(), "agnes"),
        (typeof(RemoteDeepSeekModelConfig), static () => new RemoteDeepSeekModelConfig(), "deepseek"),
        (typeof(RemoteSensenovaModelConfig), static () => new RemoteSensenovaModelConfig(), "sensenova"),
        (typeof(RemoteZhipuModelConfig), static () => new RemoteZhipuModelConfig(), "zhipuai"),
        (typeof(RemoteCommandCodeModelConfig), static () => new RemoteCommandCodeModelConfig(), "commandcode"),
        (typeof(RemoteOpenCodeModelConfig), static () => new RemoteOpenCodeModelConfig(), "opencode"),
        (typeof(RemoteOpenRouterModelConfig), static () => new RemoteOpenRouterModelConfig(), ""),
        (typeof(RemoteModelConfig), static () => new RemoteModelConfig(), ""),
    ];

    /// <summary>供应商配置类型（不含通用兜底），顺序即界面显示顺序</summary>
    public static IReadOnlyList<Type> ProviderTypes { get; } =
        [.. Entries.Take(Entries.Length - 1).Select(entry => entry.Type)];

    /// <summary>全部可实例化的配置类型：供应商 + 通用兜底</summary>
    public static IReadOnlyList<Type> AllConfigTypes { get; } = [.. Entries.Select(entry => entry.Type)];

    /// <summary>
    /// 按类型建默认配置实例（显式工厂，无反射）。
    /// 非注册表内的类型返回 null——配置类型的全集就在这张表里。
    /// </summary>
    public static BaseRemoteModelConfig? CreateDefault(Type configType)
    {
        foreach ((Type type, Func<BaseRemoteModelConfig> factory, _) in Entries)
        {
            if (type == configType) return factory();
        }
        return null;
    }

    /// <summary>
    /// 配置类型 → models.dev 的 provider 键；未知或没有对应键（OpenRouter 这类自带目录的）返回空串
    /// </summary>
    /// <param name="configType">远程配置类型</param>
    public static string ProviderKeyOf(Type configType)
    {
        foreach ((Type type, _, string providerKey) in Entries)
        {
            if (type == configType) return providerKey;
        }
        return "";
    }

    /// <summary>
    /// models.dev 的 provider 键 → 已登记的配置类型。
    /// 「更多供应商」选到已有预设的家时走这条：直接切到那家预设行，不再建一条自定义配置
    /// （预设那份带家特有逻辑，用自定义那份是降级）。
    /// </summary>
    /// <param name="providerKey">models.dev 的 provider 键</param>
    /// <param name="configType">对应的配置类型</param>
    /// <returns>有对应预设为 true</returns>
    public static bool TryGetConfigType(string providerKey, out Type configType)
    {
        if (providerKey.Length > 0)
        {
            foreach ((Type type, _, string key) in Entries)
            {
                if (key.Length > 0 && string.Equals(key, providerKey, StringComparison.OrdinalIgnoreCase))
                {
                    configType = type;
                    return true;
                }
            }
        }

        configType = typeof(RemoteModelConfig);
        return false;
    }

    /// <summary>
    /// 按存档里的 ConfigType 名字解析配置类型（兼容类名与全名；未知返回 null）。
    /// 配置类被改名/删除后旧存档会查不到——与原先 Type.GetType 的兜底路径一致。
    /// </summary>
    public static Type? ResolveConfigType(string? configTypeName)
    {
        if (string.IsNullOrEmpty(configTypeName)) return null;
        foreach ((Type type, _, _) in Entries)
        {
            if (type.Name == configTypeName || type.FullName == configTypeName) return type;
        }
        return null;
    }
}
