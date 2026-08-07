/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 模型上下文窗口的解析：喂给模型的历史该按多大的窗口装，依据只能来自模型本身。
///
/// 远程与本地的来源完全不同，兜底值也差两个数量级，因此**绝不共用一个常量**——
/// 一个加载在 4096 上下文的本地模型若拿到远程那份兜底，每一轮都会必然溢出。
/// </summary>
public static class ModelContextResolver
{
    /// <summary>远程模型取不到上下文时的兜底。偏大是有意的：预算过大的后果是服务端当场报错
    /// （响亮、去模型编辑里填个数即可），预算过小的后果是长对话被静默截断（没人会发现）。</summary>
    public const int RemoteFallback = 200_000;

    /// <summary>本地模型取不到运行期上下文时的兜底。正常路径永远走实际加载值，这里只防万一。</summary>
    public const int LocalFallback = 8192;

    /// <summary>
    /// 解析远程模型的上下文窗口
    /// </summary>
    /// <param name="model">远程模型</param>
    /// <returns>上下文窗口 token 数</returns>
    public static int ResolveRemote(RemoteModelInfo model)
    {
        BaseRemoteModelConfig config = model.Config;
        return ResolveRemote(config.ContextLength, config.ModelId, config.ModelIdVariants);
    }

    /// <summary>
    /// 解析远程模型的上下文窗口（纯函数形态，便于单测）。
    ///
    /// 顺序是「用户填的 → 预设表 → 兜底」。用户值永远优先：模型标称 1M 却实测拒收时，
    /// 模型编辑窗里那个 <c>ContextLength</c> 是唯一能压住它的地方，运行期补齐不许覆盖它。
    ///
    /// 查表这一步是 1.16 之后新加的：预设表原先只在「新建模型」对话框里预填一次，
    /// 早先添加或走编辑路径存下来的配置会把 0 固化下来，运行期不补就全落到兜底上。
    /// 也因此那张表从「默认值」升格成了「运行期依据」，得当代码维护。
    /// </summary>
    /// <param name="configured">配置里存的上下文长度，0 表示未设置</param>
    /// <param name="modelId">模型标识，用于查预设表</param>
    /// <param name="variants">该供应商的预设表</param>
    /// <returns>上下文窗口 token 数</returns>
    public static int ResolveRemote(int configured, string? modelId,
        IReadOnlyDictionary<string, RemoteModelIdVariant>? variants)
    {
        if (configured > 0) return configured;
        if (string.IsNullOrEmpty(modelId)) return RemoteFallback;

        if (variants != null && variants.TryGetValue(modelId, out RemoteModelIdVariant? variant) &&
            variant.ContextLength > 0)
        {
            return variant.ContextLength;
        }

        // 自己那张表没命中就查全局表。配置类被改名或删掉之后,存档里的 ConfigType 会指向一个
        // 不存在的类,RemoteModelManager 的类型修复对它无能为力,配置于是退化成通用的
        // RemoteModelConfig——那个类的 ModelIdVariants 是空表,查什么都查不到。
        // ModelId 本身是全局唯一的(各家的模型名不会撞),所以按它兜底既安全又能自愈
        if (GlobalVariants.Value.TryGetValue(modelId, out int contextLength)) return contextLength;

        return RemoteFallback;
    }

    /// <summary>
    /// 全供应商的 ModelId → 上下文长度总表，反射一次装配。
    /// 新增供应商配置类时自动并入，不必登记。
    /// </summary>
    private static readonly Lazy<IReadOnlyDictionary<string, int>> GlobalVariants = new(BuildGlobalVariants);

    private static Dictionary<string, int> BuildGlobalVariants()
    {
        Dictionary<string, int> map = new(StringComparer.OrdinalIgnoreCase);
        foreach (Type type in typeof(BaseRemoteModelConfig).Assembly.GetTypes())
        {
            if (type.IsAbstract || !type.IsSubclassOf(typeof(BaseRemoteModelConfig))) continue;

            try
            {
                if (Activator.CreateInstance(type) is not BaseRemoteModelConfig config) continue;
                foreach (KeyValuePair<string, RemoteModelIdVariant> pair in config.ModelIdVariants)
                {
                    //先到先得:同一个 ModelId 在两家都声明过的话,谁先谁算,反正数值该一致
                    if (pair.Value.ContextLength > 0) map.TryAdd(pair.Key, pair.Value.ContextLength);
                }
            }
            catch (Exception e)
            {
                Log.Warning($"Collect model id variants from '{type.Name}' failed: {e.Message}");
            }
        }

        return map;
    }

    /// <summary>
    /// 解析本地模型的上下文窗口。依据是运行期实际加载的大小，不是模型元数据里的标称值——
    /// auto 档下实际加载值按可用内存缩放，可能远小于标称（元数据写 128k 的模型可能只加载了 8k）。
    /// </summary>
    /// <param name="runtimeContextSize">运行期解析出的上下文大小，未知时传 0</param>
    /// <returns>上下文窗口 token 数</returns>
    public static int ResolveLocal(int runtimeContextSize)
    {
        return runtimeContextSize > 0 ? runtimeContextSize : LocalFallback;
    }
}
