/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.Browser;

/// <summary>
/// 标签页上的下载：落到哪、哪些还在下，动作之后等它们下完再交结果（调度见 <see cref="PuppeteerDownloads"/>）
/// </summary>
internal sealed partial class PuppeteerTab
{
    private const int DownloadWaitMs = 15_000; //动作点出来的下载等这么久；再大的下完随下一次结果说

    private readonly ConcurrentDictionary<string, Download> _downloads = new(); //guid → 进行中的下载
    private DateTime _lastDownloadStarted = DateTime.MinValue;

    /// <summary>下载落到的目录（会话草稿目录下）；空表示不知道，下完留在中转目录</summary>
    public string DownloadDirectory { get; set; } = string.Empty;

    /// <summary>
    /// 这个框架是不是本页的（主框架或 iframe）
    /// </summary>
    /// <param name="frameId">框架 id</param>
    /// <returns>是为 true</returns>
    public bool OwnsFrame(string frameId) => !_page.IsClosed && _page.Frames.Any(frame => frame.Id == frameId);

    /// <summary>
    /// 本页开始了一个下载
    /// </summary>
    /// <param name="guid">下载 id</param>
    /// <param name="fileName">文件名</param>
    public void DownloadStarted(string guid, string fileName)
    {
        _lastDownloadStarted = DateTime.UtcNow;
        _downloads[guid] = new Download(_lastDownloadStarted,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        Log.Debug($"[Browser] download started: {fileName}");
    }

    /// <summary>
    /// 本页的一个下载结束了：结果随下一次结果交给模型
    /// </summary>
    /// <param name="guid">下载 id</param>
    /// <param name="note">给模型的说明（存到了哪、或失败了）</param>
    public void DownloadEnded(string guid, string note)
    {
        _notes.Enqueue(note);
        if (_downloads.TryRemove(guid, out Download? done)) done.Finished.TrySetResult();
    }

    // 这一步点出来的下载等下完再拍，模型这一步就拿到路径、下一步能直接 Read；之前没下完的大文件不再拖每一步
    private async Task WaitDownloadsAsync(DateTime since)
    {
        Task[] started = _downloads.Values.Where(download => download.Started >= since)
            .Select(download => download.Finished.Task).ToArray();
        if (started.Length == 0) return;
        Task all = Task.WhenAll(started);
        if (await Task.WhenAny(all, Task.Delay(DownloadWaitMs)) != all)
        {
            _notes.Enqueue("A download is still in progress; you'll be told where it was saved once it finishes.");
        }
    }

    // 导航到文件地址时导航先中止、下载事件稍后才到：等一小会儿再认
    private async Task<bool> DownloadStartsAsync(DateTime since)
    {
        for (int i = 0; i < 10 && _lastDownloadStarted < since; i++) await Task.Delay(200);
        return _lastDownloadStarted >= since;
    }

    private sealed record Download(DateTime Started, TaskCompletionSource Finished);
}
