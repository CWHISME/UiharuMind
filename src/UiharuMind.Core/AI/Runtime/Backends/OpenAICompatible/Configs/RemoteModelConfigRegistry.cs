using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.Configs.RemoteAI;

/// <summary>
/// 远程模型供应商配置的显式注册表：替代原先「扫整程序集找 <see cref="BaseRemoteModelConfig"/> 子类 +
/// <see cref="Activator.CreateInstance"/>」的反射装配（ModelContextResolver / RemoteModelEditViewData /
/// RemoteModelManager 三处共用）。类型被 typeof 直接引用、实例走显式工厂，AOT/裁剪友好；
/// 条目顺序固定，即界面供应商顺序与变体表合并顺序（先到先得）。
/// 新增供应商：在这里登记即可，三处消费自动生效。
/// </summary>
public static class RemoteModelConfigRegistry
{
    // 配置类型全集。前四项为供应商（顺序即界面显示顺序与变体表先到先得的合并顺序），
    // 通用兜底 RemoteModelConfig 永远放最后、不作为供应商展示。
    private static readonly (Type Type, Func<BaseRemoteModelConfig> Factory)[] Entries =
    [
        (typeof(RemoteAgnesModelConfig), static () => new RemoteAgnesModelConfig()),
        (typeof(RemoteDeepSeekModelConfig), static () => new RemoteDeepSeekModelConfig()),
        (typeof(RemoteSensenovaModelConfig), static () => new RemoteSensenovaModelConfig()),
        (typeof(RemoteZhipuModelConfig), static () => new RemoteZhipuModelConfig()),
        (typeof(RemoteCommandCodeModelConfig), static () => new RemoteCommandCodeModelConfig()),
        (typeof(RemoteOpenCodeModelConfig), static () => new RemoteOpenCodeModelConfig()),
        (typeof(RemoteOpenRouterModelConfig), static () => new RemoteOpenRouterModelConfig()),
        (typeof(RemoteModelConfig), static () => new RemoteModelConfig()),
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
        foreach ((Type type, Func<BaseRemoteModelConfig> factory) in Entries)
        {
            if (type == configType) return factory();
        }
        return null;
    }

    /// <summary>
    /// 按存档里的 ConfigType 名字解析配置类型（兼容类名与全名；未知返回 null）。
    /// 配置类被改名/删除后旧存档会查不到——与原先 Type.GetType 的兜底路径一致。
    /// </summary>
    public static Type? ResolveConfigType(string? configTypeName)
    {
        if (string.IsNullOrEmpty(configTypeName)) return null;
        foreach ((Type type, _) in Entries)
        {
            if (type.Name == configTypeName || type.FullName == configTypeName) return type;
        }
        return null;
    }
}
