using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;
using UiharuMind.Core.AI.Chat.Group.Away;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Group;

/// <summary>
/// 群右栏的离席块（ADR 0055）：没在离席时是目标框、化身模型与开始按钮；离席中是状态、倒计时与几个动作。
/// 只在智能体群里有。状态跟着 <see cref="GroupAwayController.StatusChanged"/> 刷新，倒计时每秒走一格
/// </summary>
public sealed partial class GroupAwayViewData : ObservableObject, IDisposable
{
    private readonly ChatSession _group;
    private readonly GroupAwayController _away;
    private readonly Func<DateTimeOffset> _now;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private GroupAwayStatus? _status;
    private bool _disposed; //投到 UI 线程的刷新可能在视图关掉之后才到

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    private string _goal = string.Empty;

    [ObservableProperty] private SessionModelOption _selectedModel;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话（智能体群）</param>
    /// <param name="away">离席控制器；null 取应用里的那一个</param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    public GroupAwayViewData(ChatSession group, GroupAwayController? away = null, Func<DateTimeOffset>? now = null)
    {
        _group = group;
        _away = away ?? GroupAwayController.Instance;
        _now = now ?? (() => DateTimeOffset.Now);
        ModelOptions = SessionModelOption.SnapshotWithDefault(Loc.Text(LangKey.GroupModelFollowGlobal));
        _selectedModel = DefaultModel(group, ModelOptions);
        _status = _away.StatusOf(group.SessionId);
        _ticker.Tick += (_, _) => OnPropertyChanged(nameof(CountdownLine));
        _away.StatusChanged += OnStatusChanged;
        SyncTicker();
    }

    /// <summary>化身模型的选项：「跟随全局」+ 当前模型清单</summary>
    public IReadOnlyList<SessionModelOption> ModelOptions { get; }

    /// <summary>在离席</summary>
    public bool IsAway => _status != null;

    /// <summary>没在离席（表单显隐）</summary>
    public bool IsIdle => _status == null;

    /// <summary>离席中的状态行</summary>
    public string StatusLine => _status switch
    {
        null => string.Empty,
        { IsAvatarRunning: true } => Loc.Text(LangKey.GroupAwayAvatarThinking),
        _ => Loc.Text(LangKey.GroupAwayStatusFormat, _status.AvatarTurns),
    };

    /// <summary>延迟唤醒的倒计时；没在等为空</summary>
    public string CountdownLine => _status?.WakeAt is { } wakeAt
        ? Loc.Text(LangKey.GroupAwayCountdownFormat, CountdownText(wakeAt - _now()))
        : string.Empty;

    /// <summary>有没有倒计时（「立即唤醒」与倒计时行显隐）</summary>
    public bool HasCountdown => _status?.WakeAt != null;

    /// <summary>
    /// 倒计时的写法：不足一小时写「分:秒」，否则「时:分:秒」；过了点按 0 算
    /// </summary>
    /// <param name="remaining">剩余时间</param>
    /// <returns>文本</returns>
    public static string CountdownText(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
        return remaining.TotalHours >= 1
            ? $"{(int)remaining.TotalHours}:{remaining.Minutes:00}:{remaining.Seconds:00}"
            : $"{remaining.Minutes}:{remaining.Seconds:00}";
    }

    /// <summary>摘掉订阅、停掉倒计时</summary>
    public void Dispose()
    {
        _disposed = true;
        _away.StatusChanged -= OnStatusChanged;
        _ticker.Stop();
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private void Start()
    {
        string? model = SelectedModel.IsDefault ? null : SelectedModel.ModelName;
        if (_away.Start(_group, Goal, model)) Goal = string.Empty;
    }

    private bool CanStart() => !string.IsNullOrWhiteSpace(Goal);

    [RelayCommand]
    private void End() => _away.End(_group.SessionId);

    [RelayCommand]
    private void WakeNow() => _away.WakeNow(_group.SessionId);

    [RelayCommand]
    private void OpenAvatar()
    {
        // 离席结束后化身会话还在，照样点得开
        string? id = _status?.AvatarSessionId ?? GroupAvatar.MetaOf(_group.SessionId)?.SessionId;
        if (id != null) SubSessionWindowOpener.Open(id);
    }

    // 默认用主持人的模型（没有主持人取第一位成员的）：化身做的是判断活，值得给群里最强的那个
    private static SessionModelOption DefaultModel(ChatSession group, IReadOnlyList<SessionModelOption> options)
    {
        string? memberId = group.GroupHostSessionId ?? group.GroupMemberSessionIds.FirstOrDefault();
        string? modelName = memberId == null ? null : SessionManager.Instance.GetMeta(memberId)?.SessionModelName;
        return options.FirstOrDefault(x => !x.IsDefault && x.ModelName == modelName) ?? options[0];
    }

    private void OnStatusChanged(string groupId)
    {
        if (groupId != _group.SessionId) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed) return;
            _status = _away.StatusOf(_group.SessionId);
            OnPropertyChanged(nameof(IsAway));
            OnPropertyChanged(nameof(IsIdle));
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(CountdownLine));
            OnPropertyChanged(nameof(HasCountdown));
            SyncTicker();
        });
    }

    private void SyncTicker()
    {
        if (HasCountdown) _ticker.Start();
        else _ticker.Stop();
    }
}
