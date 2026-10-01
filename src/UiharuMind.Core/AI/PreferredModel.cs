using UiharuMind.Core.AI.Core;

namespace UiharuMind.Core.AI;

/// <summary>
/// 没有点名时挑哪个模型。视觉模型也能聊天：正在用的、收藏的不因为能看图就被跳过；
/// 只有什么都没定的兜底才优先挑不带视觉的——专门的视觉模型（如 glm-4v）聊天多半偏弱
/// </summary>
public static class PreferredModel
{
    /// <summary>
    /// 按「正在用 → 收藏的远程 → 收藏的本地 → 未收藏的远程」挑一个
    /// </summary>
    /// <param name="current">全局当前模型</param>
    /// <param name="favorites">收藏，按顺序</param>
    /// <param name="byName">按名字索引的全部模型</param>
    /// <param name="all">全部模型，按列表顺序</param>
    /// <param name="needsVision">要看图；为 false 时视觉模型同样可用</param>
    /// <returns>挑中的模型；没有可用的为 null</returns>
    public static ModelRunningData? Pick(ModelRunningData? current, IReadOnlyList<string> favorites,
        IReadOnlyDictionary<string, ModelRunningData> byName, IReadOnlyList<ModelRunningData> all, bool needsVision)
    {
        bool Usable(ModelRunningData model) => !needsVision || model.IsVisionModel;

        if (current != null && Usable(current)) return current;

        List<ModelRunningData> favored = favorites
            .Select(name => byName.GetValueOrDefault(name))
            .OfType<ModelRunningData>()
            .Where(Usable)
            .ToList();
        // 不自动加载未收藏的本地模型
        return favored.FirstOrDefault(m => m.IsRemoteModel)
               ?? favored.FirstOrDefault(m => !m.IsRemoteModel)
               ?? all.FirstOrDefault(m => m.IsRemoteModel && m.IsVisionModel == needsVision)
               ?? all.FirstOrDefault(m => m.IsRemoteModel && Usable(m));
    }
}
