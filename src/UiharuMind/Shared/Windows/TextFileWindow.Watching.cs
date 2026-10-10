using System;
using System.Threading.Tasks;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Shared.Windows;

/// <summary>
/// 文本窗盯着磁盘上的文件：智能体改了正开着的文件时，没未保存改动就原地重新载入（光标与滚动不动，
/// 撤销能拿回旧内容），有改动就出一条提示让人选；保存前文件被改过也先问，免得旧内容盖掉别人的修改。
///
/// 判「变没变」只比 <see cref="FileStamp"/>：自己存盘后记下新指纹，自己那次写盘的事件就不算外部修改。
/// 只在窗口开着时盯，藏进缓存就停；再激活时补查一次，藏着那段时间的修改也不漏
/// </summary>
public partial class TextFileWindow
{
    private FileChangeWatcher? _diskWatcher;
    private FileStamp _diskStamp; //与编辑器内容对得上的那份磁盘指纹
    private int _watchVersion; //每停一次加一：后台挂好的监视回来时对不上就丢掉
    private bool _isWatchStarting;

    /// <summary>换了文档：重新记指纹、收起提示、换着盯新路径</summary>
    private void ResetDiskTracking()
    {
        StopWatchingDisk();
        DiskChangeBar.IsVisible = false;
        _diskStamp = _document.FilePath != null ? FileStamp.Read(_document.FilePath) : default;
        StartWatchingDisk();
    }

    // macOS 上挂 FileSystemWatcher 要同步等系统事件流起来（实测五六十毫秒），放后台挂，免得卡打开
    private void StartWatchingDisk()
    {
        if (_diskWatcher != null || _isWatchStarting || _document.FilePath is not { } path) return;
        _isWatchStarting = true;
        int version = _watchVersion;
        Action onChanged = () => Dispatcher.UIThread.Post(() => _ = CheckDiskChangeAsync());
        _ = Task.Run(() =>
        {
            try
            {
                return new FileChangeWatcher(path, onChanged);
            }
            catch (Exception e)
            {
                // 目录没了或系统监视句柄用尽：不盯也照样能编辑，保存前的比对仍在
                Log.Warning($"[TextFile] watch failed: {e.Message}");
                return null;
            }
        }).ContinueWith(task => Dispatcher.UIThread.Post(() =>
        {
            FileChangeWatcher? watcher = task.Result;
            if (version != _watchVersion)
            {
                watcher?.Dispose();
                return;
            }

            _isWatchStarting = false;
            _diskWatcher = watcher;
        }), TaskScheduler.Default);
    }

    private void StopWatchingDisk()
    {
        _watchVersion++;
        _isWatchStarting = false;
        _diskWatcher?.Dispose();
        _diskWatcher = null;
    }

    private void OnWindowActivated(object? sender, EventArgs e)
    {
        StartWatchingDisk();
        _ = CheckDiskChangeAsync();
    }

    /// <summary>比一次磁盘指纹，变了就按有没有未保存改动处理（监视事件与窗口激活时调）</summary>
    internal async Task CheckDiskChangeAsync()
    {
        if (_document.FilePath == null || !IsVisible) return;
        FileStamp stamp = FileStamp.Read(_document.FilePath);
        if (stamp == _diskStamp) return;

        if (!stamp.Exists) ShowDiskChangeBar(LangKey.TextFileDiskDeleted, canReload: false);
        else if (_document.IsDirty) ShowDiskChangeBar(LangKey.TextFileDiskChanged, canReload: true);
        else await ReloadFromDiskAsync();
    }

    private void ShowDiskChangeBar(LangKey textKey, bool canReload)
    {
        DiskChangeText.Text = Loc.Text(textKey);
        ReloadFromDiskButton.IsVisible = canReload;
        DiskChangeBar.IsVisible = true;
    }

    private async Task ReloadFromDiskAsync()
    {
        if (_document.FilePath is not { } path) return;
        FileStamp stamp = FileStamp.Read(path);
        TextFileReadResult result = await TextFileCodec.ReadTextAsync(path, default);
        // 读的这一会儿换了文档就作罢
        if (path != _document.FilePath) return;
        if (!result.Success || result.Text == null)
        {
            MessageService.ShowNotification($"{Loc.Text(LangKey.TextFileDiskReloadFailed)} {result.ErrorCode}",
                severity: MessageSeverity.Error);
            return;
        }

        _suppressDirty = true;
        DocumentView.ReplaceText(result.Text);
        _suppressDirty = false;
        _document.IsDirty = false;
        _diskStamp = stamp;
        DiskChangeBar.IsVisible = false;
        RefreshDirtyState();
        RefreshMenuStates();
        UpdateStatusBar();
    }

    /// <summary>
    /// 保存前：文件在打开后被别处改过，先问要不要覆盖。被删了不问，保存就是重新建出来
    /// </summary>
    /// <returns>可以接着写返回 true</returns>
    private async Task<bool> ConfirmOverwriteDiskChangeAsync(string path)
    {
        FileStamp stamp = FileStamp.Read(path);
        if (!stamp.Exists || stamp == _diskStamp) return true;
        return await MessageService.ConfirmAsync(Loc.Text(LangKey.TextFileDiskOverwriteConfirm));
    }

    /// <summary>自己写完盘：记下新指纹，这次写盘引起的事件就不算外部修改</summary>
    private void OnSavedToDisk()
    {
        _diskStamp = _document.FilePath != null ? FileStamp.Read(_document.FilePath) : default;
        DiskChangeBar.IsVisible = false;
    }

    private void ReloadFromDiskButton_Click(object? sender, RoutedEventArgs e)
    {
        _ = ReloadFromDiskAsync();
    }

    /// <summary>保留编辑器里的：认下磁盘现状，之后保存直接覆盖、不再问</summary>
    private void KeepMineButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_document.FilePath != null) _diskStamp = FileStamp.Read(_document.FilePath);
        DiskChangeBar.IsVisible = false;
    }
}
