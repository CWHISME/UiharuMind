using System;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads.Image;

/// <summary>
/// 生图分类左栏的一个家族
/// </summary>
/// <param name="family">家族</param>
/// <param name="language">界面语言</param>
public partial class ImageFamilyListItem(ImageCatalogFamily family, string language) : ObservableObject
{
    [ObservableProperty] private bool _isInstalled;

    /// <summary>
    /// 家族
    /// </summary>
    public ImageCatalogFamily Family { get; } = family;

    public string Title => Family.Name;

    public bool IsRecommended => Family.Recommended;

    /// <summary>
    /// 没实际出过图
    /// </summary>
    public bool IsUntested => !Family.Tested;

    /// <summary>
    /// 第二行：文生图 / 可改图 · 最小一档多大
    /// </summary>
    public string SubText => Loc.Text(LangKey.ImageFamilySummary,
        Loc.Text(Family.SupportsEditing ? LangKey.ImageFamilyEditing : LangKey.ImageFamilyTextToImage),
        GameUtils.FormatBytes(Family.Tiers.Min(x => x.TotalSize)));

    /// <summary>
    /// 是否匹配搜索词（名称、id 或简介）
    /// </summary>
    /// <param name="query">搜索词，已去空白</param>
    /// <returns>匹配为 true</returns>
    public bool Matches(string query) =>
        query.Length == 0 ||
        Family.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Family.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        Family.DescriptionFor(language).Contains(query, StringComparison.OrdinalIgnoreCase);
}
