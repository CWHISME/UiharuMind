using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Artifacts;

/// <summary>
/// 右栏产物区（方案 v6 §1.4 产出锚点）：群或单聊到底落了哪些盘，每条带路径。
/// 收集是 Core 的纯函数（<see cref="GroupArtifacts"/>），盯谁由 <see cref="IArtifactSource"/> 定，这里只管什么时候刷——
/// 草稿目录变了（文件监听）、相关会话跑完一次服务调用（写入的结果随下一次调用落盘）、跑完一轮。
/// 几路信号都合进一次防抖，收集在后台线程上做
/// </summary>
public sealed partial class ArtifactsViewData : ObservableObject, IDisposable
{
    private const int RefreshDelayMs = 400; //一阵写入合成一次刷新

    private readonly IArtifactSource _source;
    private readonly object _locker = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <summary>产物，最近改过的在前</summary>
    public ObservableCollection<ArtifactItem> Items { get; } = new();

    /// <summary>草稿目录</summary>
    public string DraftRoom => _source.DraftRoom;

    /// <summary>面板标题</summary>
    public string Title => _source.Title;

    /// <summary>面板说明</summary>
    public string Hint => _source.Hint;

    /// <summary>盘上还什么都没有</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyNotice))]
    private bool _isEmpty = true;

    /// <summary>只看草稿目录（否则草稿 + 改过的工作区文件都列）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FilterTip))]
    private bool _draftOnly;

    /// <summary>筛完一条不剩（盘上有东西，但全是工作区改的）</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEmptyNotice))]
    private bool _isDraftEmpty;

    /// <summary>空状态要不要出现（盘空，或筛完不剩）；区块整块收起时不留行距</summary>
    public bool HasEmptyNotice => IsEmpty || IsDraftEmpty;

    /// <summary>筛选钮的悬停说明，随开关变</summary>
    public string FilterTip => Loc.Text(DraftOnly ? LangKey.GroupArtifactsFilterAll : LangKey.GroupArtifactsFilterDrafts);

    /// <summary>
    /// 盯一个来源的产物
    /// </summary>
    /// <param name="source">群或单聊会话</param>
    public ArtifactsViewData(IArtifactSource source)
    {
        _source = source;
        SessionManager.Instance.SessionUsageReported += OnMemberActivity;
        SessionManager.Instance.Running.StateChanged += OnMemberActivity;
        Watch();
        ScheduleRefresh();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_locker)
        {
            _disposed = true;
            _pending?.Cancel();
            _watcher?.Dispose();
            _watcher = null;
        }

        SessionManager.Instance.SessionUsageReported -= OnMemberActivity;
        SessionManager.Instance.Running.StateChanged -= OnMemberActivity;
    }

    /// <summary>打开草稿目录（还没人写过时顺手建出来，用户往里放参考文件也方便）</summary>
    [RelayCommand]
    private void OpenDraftRoom() => App.FilesService.OpenFolder(DraftRoom);

    /// <summary>立刻重读一遍</summary>
    [RelayCommand]
    private void Refresh() => ScheduleRefresh();

    partial void OnDraftOnlyChanged(bool value) => ApplyFilter();

    // 来自后台线程
    private void OnMemberActivity(string sessionId)
    {
        if (_source.IsRelevant(sessionId)) ScheduleRefresh();
    }

    // 草稿目录还没建（没人写过）就先不盯，等第一次刷新时它出现了再挂上
    private void Watch()
    {
        lock (_locker)
        {
            if (_disposed || _watcher != null || !Directory.Exists(DraftRoom)) return;

            try
            {
                FileSystemWatcher watcher = new(DraftRoom) { IncludeSubdirectories = true };
                watcher.Created += OnDraftRoomChanged;
                watcher.Changed += OnDraftRoomChanged;
                watcher.Deleted += OnDraftRoomChanged;
                watcher.Renamed += OnDraftRoomChanged;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception)
            {
                // 盯不上就只靠成员信号与手动刷新
            }
        }
    }

    private void OnDraftRoomChanged(object sender, FileSystemEventArgs e) => ScheduleRefresh();

    private void ScheduleRefresh()
    {
        CancellationToken token;
        lock (_locker)
        {
            if (_disposed) return;
            _pending?.Cancel();
            _pending = new CancellationTokenSource();
            token = _pending.Token;
        }

        _ = RefreshAfterDelayAsync(token);
    }

    private async Task RefreshAfterDelayAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(RefreshDelayMs, token);
            IReadOnlyList<GroupArtifact> artifacts = await Task.Run(Collect, token);
            Watch();
            Dispatcher.UIThread.Post(() =>
            {
                if (token.IsCancellationRequested) return;
                ArtifactListSync.Apply(Items, artifacts);
                ApplyFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // 被下一次刷新顶掉
        }
    }

    private IReadOnlyList<GroupArtifact> Collect() => _source.Collect();

    // 切换筛选项与每次刷新后都走这里：实例不重建，只翻每行的可见开关
    private void ApplyFilter()
    {
        bool anyVisible = false;
        foreach (ArtifactItem item in Items)
        {
            bool shown = !DraftOnly || item.IsDraft;
            item.MatchesFilter = shown;
            if (shown) anyVisible = true;
        }

        IsEmpty = Items.Count == 0;
        IsDraftEmpty = DraftOnly && Items.Count > 0 && !anyVisible;
    }
}
