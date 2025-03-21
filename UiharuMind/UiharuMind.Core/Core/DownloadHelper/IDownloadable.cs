/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

namespace UiharuMind.Core.Core.Utils;

public interface IDownloadable
{
    public string Name { get; }
    public string DownloadUrl { get; }

    public bool IsDownloaded { get; set; }
    public bool IsNotAllowDelete { get; }

    /// <summary>
    /// 如果 DownloadFileName 为空，则直接以下载文件名为文件名下载至 DownloadDirectory
    /// 下载完成后，会被 DownloadableItemData 记录
    /// </summary>
    public string? DownloadFileName { get; }

    /// <summary>
    /// 如果 DownloadDirectory 也为空，则下载至缓存目录下
    /// 下载完成后，会被 DownloadableItemData 记录
    /// </summary>
    public string? DownloadDirectory { get; }
}