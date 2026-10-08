using System;
using System.Collections.Generic;
using System.Linq;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 一个量化在本机的状态
/// </summary>
public enum EQuantDownloadState
{
    NotDownloaded,
    Queued,
    Downloading,
    Paused,
    Failed,
    Partial,
    Downloaded,
    NameConflict
}

/// <summary>
/// 组成量化的一个文件此刻的样子
/// </summary>
/// <param name="Size">文件大小</param>
/// <param name="Exists">已落盘</param>
/// <param name="JobState">队列里未完成的下载项状态，没有为 null</param>
/// <param name="ReceivedBytes">已下字节（来自下载项或 .part.json）</param>
public readonly record struct QuantFileSnapshot(long Size, bool Exists, EDownloadJobState? JobState, long ReceivedBytes);

/// <summary>
/// 量化状态与进度
/// </summary>
/// <param name="State">状态</param>
/// <param name="Percent">0~100，没有进度意义的状态为 0</param>
public readonly record struct QuantDownloadStatus(EQuantDownloadState State, int Percent)
{
    /// <summary>
    /// 能点下载（或继续、重试）
    /// </summary>
    public bool CanDownload => State is EQuantDownloadState.NotDownloaded or EQuantDownloadState.Paused
        or EQuantDownloadState.Failed or EQuantDownloadState.Partial;

    /// <summary>
    /// 状态行的文字
    /// </summary>
    public string StatusText => State switch
    {
        EQuantDownloadState.Queued => Loc.Text(LangKey.ModelDownloadStateQueued),
        EQuantDownloadState.Downloading => Loc.Text(LangKey.ModelDownloadStateDownloading, Percent),
        EQuantDownloadState.Paused => Loc.Text(LangKey.ModelDownloadStatePausedPercent, Percent),
        EQuantDownloadState.Failed => Loc.Text(LangKey.ModelDownloadStateFailedShort),
        EQuantDownloadState.Partial => Loc.Text(LangKey.ModelDownloadStatePartial, Percent),
        EQuantDownloadState.Downloaded => Loc.Text(LangKey.ModelDownloadStateDownloaded),
        EQuantDownloadState.NameConflict => Loc.Text(LangKey.ModelDownloadStateNameConflict),
        _ => Loc.Text(LangKey.ModelDownloadStateNotDownloaded)
    };

    /// <summary>
    /// 下载按钮上的字：继续 / 重试 / 下载
    /// </summary>
    public string ActionText => State switch
    {
        EQuantDownloadState.Paused or EQuantDownloadState.Partial => Loc.Text(LangKey.ModelDownloadResume),
        EQuantDownloadState.Failed => Loc.Text(LangKey.ModelDownloadRetry),
        _ => Loc.Text(LangKey.ModelDownloadAction)
    };

    /// <summary>
    /// 该带一条进度
    /// </summary>
    public bool ShowsProgress => State is EQuantDownloadState.Downloading or EQuantDownloadState.Paused
        or EQuantDownloadState.Partial or EQuantDownloadState.Queued;

    /// <summary>
    /// 把各文件的样子合成量化的状态。下载项的状态优先于磁盘上的残留
    /// </summary>
    /// <param name="files">组成文件</param>
    /// <param name="hasNameConflict">本地已有同名模型（不在这个位置）</param>
    /// <returns>状态</returns>
    public static QuantDownloadStatus Resolve(IReadOnlyList<QuantFileSnapshot> files, bool hasNameConflict)
    {
        if (files.Count > 0 && files.All(x => x.Exists)) return new(EQuantDownloadState.Downloaded, 100);
        if (hasNameConflict) return new(EQuantDownloadState.NameConflict, 0);

        long total = files.Sum(x => x.Size);
        long received = files.Sum(x => x.Exists ? x.Size : x.ReceivedBytes);
        int percent = total <= 0 ? 0 : (int)Math.Clamp(received * 100 / total, 0, 99);

        if (files.Any(x => x.JobState == EDownloadJobState.Running)) return new(EQuantDownloadState.Downloading, percent);
        if (files.Any(x => x.JobState == EDownloadJobState.Queued)) return new(EQuantDownloadState.Queued, percent);
        if (files.Any(x => x.JobState == EDownloadJobState.Failed)) return new(EQuantDownloadState.Failed, percent);
        if (files.Any(x => x.JobState == EDownloadJobState.Paused)) return new(EQuantDownloadState.Paused, percent);
        return received > 0
            ? new(EQuantDownloadState.Partial, percent)
            : new(EQuantDownloadState.NotDownloaded, 0);
    }
}
