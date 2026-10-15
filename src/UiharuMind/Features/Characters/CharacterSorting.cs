using System;
using System.Globalization;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Characters;

/// <summary>
/// 角色排序的统一口径：三处角色列表（工作台左栏 / 内联选择器 / 建群挑人）共用同一份比较器，
/// 免得各排各的、观感不一致。
///
/// 顺序：<b>收藏恒置顶 → 分区轴 → 作品分组 → 角色名</b>。
/// 分区轴按场景由调用方给排名（左栏普通角色在前、智能体群智能体在前；无分区场景传恒等），
/// 收藏是显式的常用标记，永远排在分区/作品轴前面——因此收藏一张卡不会被打散到它本来所属的分区里。
/// </summary>
public static class CharacterSorting
{
    /// <summary>
    /// 角色名/作品名的比较器：固定中文拼音序。
    /// 角色名是数据不是界面文案，不该跟着界面语言漂移（中文界面拼音序、英文界面码位序，
    /// 同一份列表两种排法正是从前观感不稳定的来源之一）。
    /// </summary>
    private static readonly StringComparer NameComparer =
        StringComparer.Create(new CultureInfo(LanguageUtils.ChineseSimplified), ignoreCase: false);

    /// <summary>
    /// 比较两张角色卡
    /// </summary>
    /// <param name="x">角色</param>
    /// <param name="y">角色</param>
    /// <param name="kindRank">分区轴排名键（小的在前）；不分区时传 <c>_ =&gt; 0</c></param>
    /// <returns>比较结果</returns>
    public static int Compare(CharacterData x, CharacterData y, Func<CharacterData, int> kindRank)
    {
        int byFavorite = CharacterFavorites.IsFavorite(y.CharacterId).CompareTo(CharacterFavorites.IsFavorite(x.CharacterId));
        if (byFavorite != 0) return byFavorite;

        int byKind = kindRank(x).CompareTo(kindRank(y));
        if (byKind != 0) return byKind;

        int byWorks = CompareWorks(x.Works, y.Works);
        if (byWorks != 0) return byWorks;

        return NameComparer.Compare(x.CharacterName, y.CharacterName);
    }

    /// <summary>
    /// 作品组排序：未填作品（自定义卡居多，用户最常找）排最前，有作品的按作品名排
    /// </summary>
    /// <param name="x">作品名</param>
    /// <param name="y">作品名</param>
    /// <returns>比较结果</returns>
    private static int CompareWorks(string? x, string? y)
    {
        bool xEmpty = string.IsNullOrWhiteSpace(x);
        bool yEmpty = string.IsNullOrWhiteSpace(y);
        if (xEmpty && yEmpty) return 0;
        if (xEmpty) return -1;
        if (yEmpty) return 1;
        return NameComparer.Compare(x, y);
    }
}
