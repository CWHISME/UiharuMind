/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Skills;
using UiharuMind.Resources.Lang;

namespace UiharuMind.Features.Settings;

/// <summary>
/// 把平铺的技能条目分成「包 → 分类」两级。
///
/// 分组策略留在 UI 层:Core 只给 <see cref="SkillCatalogEntry.RelativePath"/> 这个事实,
/// 怎么切是展示决策。生态里的技能包普遍长成 <c>包名/skills/分类/技能名</c>,
/// 只按包名分会得到一个几十条的大组,等于没分。
/// </summary>
public static class SkillGrouping
{
    /// <summary>
    /// 分组
    /// </summary>
    /// <param name="entries">平铺的技能条目</param>
    /// <returns>分好组的列表：本地技能置顶，其余按包名排序</returns>
    public static List<SkillGroupItem> Build(IEnumerable<SkillCatalogEntry> entries)
    {
        List<SkillGroupItem> groups = entries
            .Select(entry => (entry, path: Split(entry.RelativePath)))
            .GroupBy(x => PackageOf(x.path), StringComparer.Ordinal)
            .Select(package => new SkillGroupItem(
                package.Key.Length > 0 ? package.Key : Lang.AgentSkillsLocalGroup,
                package.Key.Length == 0,
                package
                    .GroupBy(x => CategoryOf(x.path), StringComparer.Ordinal)
                    .OrderBy(x => x.Key.Length == 0 ? 0 : 1) //无分类的直接挂在包名下,排在分类之前
                    .ThenBy(x => x.Key, StringComparer.Ordinal)
                    .Select(category => new SkillCategoryItem(
                        category.Key,
                        category.Select(x => new SkillDisplayItem(x.entry))
                            .OrderBy(x => x.Name, StringComparer.Ordinal)
                            .ToList()))
                    .ToList()))
            .OrderBy(x => x.IsLocal ? 0 : 1) //根下直接放的技能是用户自己写的,置顶
            .ThenBy(x => x.Name, StringComparer.Ordinal)
            .ToList();

        return groups;
    }

    private static string[] Split(string relativePath)
    {
        return relativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    /// <summary>包名:相对路径首段。技能就在根下(只有一段)时没有包,归本地技能</summary>
    private static string PackageOf(string[] segments)
    {
        return segments.Length >= 2 ? segments[0] : string.Empty;
    }

    /// <summary>分类:技能目录名之前的那一段。包内没有分类层时为空串,不造一个空组名出来</summary>
    private static string CategoryOf(string[] segments)
    {
        return segments.Length >= 3 ? segments[^2] : string.Empty;
    }
}

/// <summary>一个技能包(相对路径首段)</summary>
public sealed class SkillGroupItem
{
    /// <summary>包名;本地技能为本地化后的固定文案</summary>
    public string Name { get; }

    /// <summary>是否是根下直接放的技能</summary>
    public bool IsLocal { get; }

    /// <summary>包内分类</summary>
    public IReadOnlyList<SkillCategoryItem> Categories { get; }

    /// <summary>包内技能总数;组头显示用</summary>
    public int SkillCount { get; }

    public SkillGroupItem(string name, bool isLocal, IReadOnlyList<SkillCategoryItem> categories)
    {
        Name = name;
        IsLocal = isLocal;
        Categories = categories;
        SkillCount = categories.Sum(x => x.Skills.Count);
    }
}

/// <summary>包内的一个分类</summary>
public sealed class SkillCategoryItem
{
    /// <summary>分类名;包内没有分类层时为空串</summary>
    public string Name { get; }

    /// <summary>本分类下的技能</summary>
    public IReadOnlyList<SkillDisplayItem> Skills { get; }

    /// <summary>有分类名才画分类标题</summary>
    public bool HasName => Name.Length > 0;

    public SkillCategoryItem(string name, IReadOnlyList<SkillDisplayItem> skills)
    {
        Name = name;
        Skills = skills;
    }
}

/// <summary>
/// 技能列表显示项（只读展示）。启停<b>不在这里</b>——技能与工具同类，属"这个智能体有什么能力"，
/// 按角色配（见 <see cref="AgentToolConfig.DisabledSkills"/> 与角色编辑页）。
/// 「模型可自选」由 SKILL.md 自己声明，属技能包的一部分而非用户偏好，同样只读。
/// </summary>
public class SkillDisplayItem
{
    /// <summary>技能名(即目录名)</summary>
    public string Name { get; }

    /// <summary>技能描述(模型自选时的匹配依据)</summary>
    public string Description { get; }

    /// <summary>是否已成功加载</summary>
    public bool IsLoaded { get; }

    /// <summary>是否退出了模型自选(只能点名调用)</summary>
    public bool IsUserInvokedOnly { get; }

    /// <summary>
    /// 没能加载的原因。两种失败要用户做的事不同：规范校验没过要改 SKILL.md，
    /// 重名要去删掉一个包，所以提示文案里连顶掉它的那个路径一起给出
    /// </summary>
    public string LoadFailureHint { get; }

    public SkillDisplayItem(SkillCatalogEntry entry)
    {
        Name = entry.Name;
        Description = entry.Description;
        IsLoaded = entry.IsLoaded;
        IsUserInvokedOnly = !entry.IsModelInvocable;
        LoadFailureHint = entry.LoadState switch
        {
            ESkillLoadState.DuplicateName =>
                string.Format(Lang.AgentSkillsDuplicateHint, entry.DuplicateOfPath),
            ESkillLoadState.Invalid => Lang.AgentSkillsNotLoadedHint,
            _ => string.Empty,
        };
    }
}
