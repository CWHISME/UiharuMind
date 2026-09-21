/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.Core.Clipboard;

/// <summary>
/// 一条剪贴板历史在<b>列表上</b>的样子：只带预览，不带正文。
///
/// ⚠️ 历史是无上限的，正文又常常是整段代码或整篇文章——把正文一并驻留，
/// 内存就会正比于「用户复制过的全部内容」。正文按 <see cref="Id"/> 现取，
/// 见 <see cref="ClipboardHistoryStore.GetText"/>。
/// </summary>
/// <param name="Id">主键</param>
/// <param name="CreatedAt">首次进入剪贴板的时刻。<b>置顶不会改动它</b></param>
/// <param name="Preview">首行预览</param>
/// <param name="ImagePath">图片文件路径；文本条目为 null</param>
/// <param name="IsFavorite">是否收藏</param>
/// <param name="SortKey">排序键，越大越靠前</param>
public sealed record ClipboardHistoryEntry(
    long Id,
    DateTime CreatedAt,
    string Preview,
    string? ImagePath,
    bool IsFavorite,
    long SortKey)
{
    /// <summary>是否为图片条目</summary>
    public bool IsImage => !string.IsNullOrEmpty(ImagePath);
}

/// <summary>列表的筛选条件。四项可叠加，全部为默认值时即「不筛选」</summary>
/// <param name="Query">关键字；空表示不按关键字筛</param>
/// <param name="ImagesOnly">只看图片</param>
/// <param name="FavoritesOnly">只看收藏</param>
public readonly record struct ClipboardHistoryFilter(
    string? Query = null,
    bool ImagesOnly = false,
    bool FavoritesOnly = false)
{
    /// <summary>没有任何筛选条件</summary>
    public static readonly ClipboardHistoryFilter None = new();
}
