using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 本地生图模型的档位与来历怎么说给用户听
/// </summary>
public static class ImageTierText
{
    /// <summary>
    /// 档位名
    /// </summary>
    /// <param name="tierId">lite / balanced / quality</param>
    /// <returns>显示名；认不出原样返回</returns>
    public static string Label(string? tierId) => tierId switch
    {
        "lite" => Loc.Text(LangKey.ImageTierLite),
        "balanced" => Loc.Text(LangKey.ImageTierBalanced),
        "quality" => Loc.Text(LangKey.ImageTierQuality),
        _ => tierId ?? ""
    };

    /// <summary>
    /// 本地条目的来历：家族 · 档位；自定义组合就说自定义
    /// </summary>
    /// <param name="local">本地模型</param>
    /// <returns>说明</returns>
    public static string Origin(LocalImageModel local)
    {
        if (ImageModelCatalog.BuiltIn.Find(local.Family) is not { } family) return Loc.Text(LangKey.ImageModelCustomCombo);
        return $"{family.Name} · {Label(local.Tier)}";
    }
}
