using System;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 搜索排序的一个选项
/// </summary>
/// <param name="Sort">排序</param>
/// <param name="Label">显示文字</param>
public sealed record ModelSearchSortOption(EModelSearchSort Sort, string Label)
{
    /// <summary>
    /// 按当前语言建出全部选项（下载最多在前，是默认）
    /// </summary>
    /// <returns>选项</returns>
    public static ModelSearchSortOption[] CreateAll() =>
    [
        new(EModelSearchSort.Downloads, Loc.Text(LangKey.ModelSearchSortDownloads)),
        new(EModelSearchSort.Trending, Loc.Text(LangKey.ModelSearchSortTrending)),
        new(EModelSearchSort.Likes, Loc.Text(LangKey.ModelSearchSortLikes)),
        new(EModelSearchSort.Updated, Loc.Text(LangKey.ModelSearchSortUpdated)),
        new(EModelSearchSort.Created, Loc.Text(LangKey.ModelSearchSortCreated))
    ];

    public override string ToString() => Label;
}

/// <summary>
/// 列表里的一个仓库
/// </summary>
/// <param name="Repository">owner/repo</param>
/// <param name="MetricText">右侧的数（随排序：下载量、点赞数或日期）</param>
/// <param name="MetricIcon">右侧数前的图标</param>
public sealed record ModelRepoListItem(string Repository, string MetricText, string MetricIcon)
{
    /// <summary>
    /// 仓库名（不含作者）
    /// </summary>
    public string Name => Repository[(Repository.IndexOf('/') + 1)..];

    /// <summary>
    /// 作者
    /// </summary>
    public string Owner => Repository.Contains('/') ? Repository[..Repository.IndexOf('/')] : "";

    /// <summary>
    /// 按排序挑右侧显示什么：按点赞排看点赞数，按时间排看对应日期，其余看下载量
    /// </summary>
    /// <param name="summary">搜索结果</param>
    /// <param name="sort">当前排序</param>
    /// <param name="now">当前时间（算「几小时前」）</param>
    /// <returns>列表项</returns>
    public static ModelRepoListItem From(ModelRepoSummary summary, EModelSearchSort sort, DateTimeOffset now) => sort switch
    {
        EModelSearchSort.Likes => new(summary.Repository, FormatCount(summary.Likes), "star"),
        EModelSearchSort.Updated when summary.LastModified is { } time =>
            new(summary.Repository, FormatTime(time, now), "clock"),
        EModelSearchSort.Created when summary.CreatedAt is { } time =>
            new(summary.Repository, FormatTime(time, now), "clock"),
        _ => new(summary.Repository, FormatCount(summary.Downloads), "download")
    };

    private static string FormatCount(long count) => count switch
    {
        >= 1_000_000 => $"{count / 1_000_000.0:0.#}M",
        >= 1_000 => $"{count / 1_000.0:0.#}K",
        _ => count.ToString()
    };

    // 按时间排时榜首往往都是当天的，只显示日期分不出先后，一个月内用相对时间
    private static string FormatTime(DateTimeOffset time, DateTimeOffset now)
    {
        TimeSpan age = now - time;
        if (age < TimeSpan.FromMinutes(1)) return Loc.Text(LangKey.ModelRepoTimeJustNow);
        if (age < TimeSpan.FromHours(1)) return Loc.Text(LangKey.ModelRepoTimeMinutesAgo, (int)age.TotalMinutes);
        if (age < TimeSpan.FromDays(1)) return Loc.Text(LangKey.ModelRepoTimeHoursAgo, (int)age.TotalHours);
        if (age < TimeSpan.FromDays(30)) return Loc.Text(LangKey.ModelRepoTimeDaysAgo, (int)age.TotalDays);
        return time.ToLocalTime().ToString("yyyy-MM-dd");
    }
}
