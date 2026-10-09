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
    /// 本地条目的来历：家族 · 档位；不是从可下载列表装的就说自定义
    /// </summary>
    /// <param name="familyName">家族名；自定义组合为 null</param>
    /// <param name="tierId">档位 id</param>
    /// <returns>说明</returns>
    public static string Origin(string? familyName, string? tierId) =>
        familyName == null ? Loc.Text(LangKey.ImageModelCustomCombo) : $"{familyName} · {Label(tierId)}";
}
