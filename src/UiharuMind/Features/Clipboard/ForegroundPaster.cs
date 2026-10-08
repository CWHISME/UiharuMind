using System;
using System.Threading.Tasks;
using Avalonia.Input;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 把一段文字粘贴进当前在前台的别的应用：先记下剪贴板原样，放进文字、按一下粘贴键，等对方取走后还原。
/// 借用期间的变化不进剪贴板历史
/// </summary>
public sealed class ForegroundPaster
{
    /// <summary>
    /// 按下粘贴后等多久再还原：对方应用读剪贴板要时间，太早还原会粘进旧内容
    /// </summary>
    public static readonly TimeSpan RestoreDelay = TimeSpan.FromMilliseconds(800);

    private static readonly TimeSpan MonitorLag = TimeSpan.FromSeconds(2); //剪贴板监控轮询最慢一秒一次，多留些

    private readonly IPasteboard _pasteboard;
    private readonly Action<TimeSpan> _ignoreHistory;
    private readonly Func<Task> _sendPaste;

    /// <param name="pasteboard">剪贴板</param>
    /// <param name="ignoreHistory">接下来这段时间的变化不记历史</param>
    /// <param name="sendPaste">往前台应用按一下粘贴键</param>
    public ForegroundPaster(IPasteboard pasteboard, Action<TimeSpan> ignoreHistory, Func<Task> sendPaste)
    {
        _pasteboard = pasteboard;
        _ignoreHistory = ignoreHistory;
        _sendPaste = sendPaste;
    }

    /// <summary>
    /// 粘贴（界面线程上调）
    /// </summary>
    /// <param name="text">文字</param>
    /// <returns>还原剪贴板后完成</returns>
    public async Task PasteAsync(string text)
    {
        DataTransfer? saved = await _pasteboard.SaveAsync();
        _ignoreHistory(RestoreDelay + MonitorLag);
        await _pasteboard.SetTextAsync(text);
        try
        {
            await _sendPaste();
            await Task.Delay(RestoreDelay);
        }
        finally
        {
            _ignoreHistory(MonitorLag);
            await _pasteboard.RestoreAsync(saved);
        }
    }
}
