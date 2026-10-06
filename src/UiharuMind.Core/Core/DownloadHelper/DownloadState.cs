using UiharuMind.Core.Core;

namespace UiharuMind.Core.Core.DownloadHelper;

/// <summary>
/// 未下完文件的旁挂进度：从哪个地址下、总大小、每块下到哪
/// </summary>
internal sealed class DownloadState
{
    public string Url { get; set; } = "";
    public long TotalBytes { get; set; }
    public List<DownloadSegment> Segments { get; set; } = [];

    public static string PathFor(string partPath) => partPath + ".json";

    public static DownloadState? Load(string partPath)
    {
        DownloadState? state = SaveUtility.Load<DownloadState>(PathFor(partPath), SaveUtility.JsonOptions);
        // 损坏或对不上总大小的进度不可信，当没有
        if (state == null || state.Segments.Count == 0 ||
            state.Segments.Sum(x => x.End - x.Start + 1) != state.TotalBytes ||
            state.Segments.Any(x => x.Done < 0 || x.Done > x.End - x.Start + 1))
            return null;
        return state;
    }

    public void Save(string partPath)
    {
        SaveUtility.Save(PathFor(partPath), this, SaveUtility.JsonOptions);
    }

    public static DownloadState Create(string url, long totalBytes, int segments)
    {
        DownloadState state = new() { Url = url, TotalBytes = totalBytes };
        long size = totalBytes / segments;
        for (int i = 0; i < segments; i++)
        {
            long start = i * size;
            long end = i == segments - 1 ? totalBytes - 1 : start + size - 1;
            state.Segments.Add(new DownloadSegment { Start = start, End = end });
        }

        return state;
    }
}

/// <summary>
/// 一块：[Start, End] 闭区间，已下 Done 字节
/// </summary>
internal sealed class DownloadSegment
{
    public long Start { get; set; }
    public long End { get; set; }
    public long Done { get; set; }

    public long Remaining => End - Start + 1 - Done;
    public bool IsComplete => Remaining <= 0;
}
