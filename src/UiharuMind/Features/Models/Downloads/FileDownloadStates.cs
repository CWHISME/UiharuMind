using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Features.Models.Downloads;

/// <summary>
/// 某一刻各文件的下载情况：磁盘上已有、队列里正在下、还是断点续传停在哪。各分类算行状态时共用
/// </summary>
public sealed class FileDownloadStates
{
    private readonly Dictionary<string, DownloadJob> _jobs; //落盘路径 → 还没完成的下载项

    /// <param name="queue">下载队列</param>
    public FileDownloadStates(DownloadQueue queue)
    {
        _jobs = queue.Jobs
            .Where(x => x.State != EDownloadJobState.Completed)
            .GroupBy(x => x.Request.DestinationPath, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
    }

    /// <summary>
    /// 一个文件的下载情况
    /// </summary>
    /// <param name="path">落盘路径</param>
    /// <param name="size">文件大小（不知道为 0）</param>
    /// <returns>快照</returns>
    public QuantFileSnapshot Of(string path, long size)
    {
        if (File.Exists(path)) return new QuantFileSnapshot(size, true, null, size);
        if (_jobs.TryGetValue(path, out DownloadJob? job))
            return new QuantFileSnapshot(size, false, job.State, job.Progress.ReceivedBytes);
        return new QuantFileSnapshot(size, false, null, FileDownloader.ReadPartialProgress(path)?.ReceivedBytes ?? 0);
    }
}
