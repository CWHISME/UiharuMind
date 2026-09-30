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
using UiharuMind.Features.Conversation.Items;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 右栏「本群产物」（方案 v6 §1.4 产出锚点）：群到底落了哪些盘，每条带路径。
/// 收集是 Core 的纯函数（<see cref="GroupArtifacts"/>），这里只管什么时候刷——
/// 草稿目录变了（文件监听）、成员跑完一次服务调用（写入的结果随下一次调用落盘）、成员一轮跑完。
/// 几路信号都合进一次防抖，收集在后台线程上做
/// </summary>
public sealed partial class GroupArtifactsViewData : ObservableObject, IDisposable
{
    private const int RefreshDelayMs = 400; //一阵写入合成一次刷新

    private readonly ChatSession _group;
    private readonly object _locker = new();
    private FileSystemWatcher? _watcher;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    /// <summary>产物，最近改过的在前</summary>
    public ObservableCollection<GroupArtifactItem> Items { get; } = new();

    /// <summary>群的草稿目录（全群共用那一间）</summary>
    public string DraftRoom { get; }

    /// <summary>盘上还什么都没有</summary>
    [ObservableProperty] private bool _isEmpty = true;

    /// <summary>
    /// 盯一个群的产物
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupArtifactsViewData(ChatSession group)
    {
        _group = group;
        DraftRoom = GroupArtifacts.DraftRoomOf(group);
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

    // 来自后台线程
    private void OnMemberActivity(string sessionId)
    {
        if (_group.GroupMemberSessionIds.Contains(sessionId)) ScheduleRefresh();
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
                Items.Clear();
                foreach (GroupArtifact artifact in artifacts) Items.Add(new GroupArtifactItem(artifact));
                IsEmpty = Items.Count == 0;
            });
        }
        catch (OperationCanceledException)
        {
            // 被下一次刷新顶掉
        }
    }

    private IReadOnlyList<GroupArtifact> Collect()
    {
        List<(string, IReadOnlyList<string>)> members = [];
        // 退群的人写过的文件照样列
        foreach (string id in GroupMembership.EveryMemberIdOf(_group))
        {
            if (SessionManager.Instance.Load(id) is not { } member) continue;
            // 历史已卸掉的成员用缓存，不为这一张清单把整份历史读回来
            members.Add((member.CharacterData.CharacterName, GroupWrittenPaths.Of(member, _group.WorkspacePath)));
        }

        return GroupArtifacts.Collect(DraftRoom, _group.WorkspacePath, members);
    }
}

/// <summary>产物区的一行</summary>
public sealed partial class GroupArtifactItem
{
    private readonly GroupArtifact _artifact;

    /// <summary>
    /// 包一件产物
    /// </summary>
    /// <param name="artifact">产物</param>
    public GroupArtifactItem(GroupArtifact artifact)
    {
        _artifact = artifact;
    }

    /// <summary>文件名</summary>
    public string Name => Path.GetFileName(_artifact.FullPath);

    /// <summary>显示路径（草稿目录 / 工作区里的相对路径）</summary>
    public string DisplayPath => _artifact.DisplayPath;

    /// <summary>绝对路径（悬停提示）</summary>
    public string FullPath => _artifact.FullPath;

    /// <summary>在草稿目录里（否则是工作区里改的）</summary>
    public bool IsDraft => _artifact.Source == EGroupArtifactSource.DraftRoom;

    /// <summary>谁写的 · 什么时候改的</summary>
    public string MetaText
    {
        get
        {
            string time = ConversationItemFactory.TimestampText(_artifact.LastWrite);
            return _artifact.Authors.Count == 0 ? time : $"{string.Join("、", _artifact.Authors)} · {time}";
        }
    }

    /// <summary>用系统默认方式打开</summary>
    [RelayCommand]
    private void Open() => FileOpener.Open(_artifact.FullPath);

    /// <summary>在文件夹中显示</summary>
    [RelayCommand]
    private void Reveal() => FileOpener.RevealInFolder(_artifact.FullPath);
}
