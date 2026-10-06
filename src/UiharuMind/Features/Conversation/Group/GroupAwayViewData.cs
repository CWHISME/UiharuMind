using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// 群右栏的离席块（ADR 0055）：常态只有一行（状态 + 化身 + 设置），点设置开弹窗填目标、提醒、模型与无限模式。
/// 只在智能体群里有。状态跟着 <see cref="GroupAwayController.StatusChanged"/> 刷新，倒计时每秒走一格
/// </summary>
public sealed partial class GroupAwayViewData : ObservableObject, IDisposable
{
    private readonly ChatSession _group;
    private readonly GroupAwayController _away;
    private readonly IMessageService _messages;
    private readonly Func<DateTimeOffset> _now;
    private readonly DispatcherTimer _ticker = new() { Interval = TimeSpan.FromSeconds(1) };
    private GroupAwayStatus? _status;
    private SessionUsageStats? _avatarUsage; //化身会话还没建为 null
    private bool _disposed; //投到 UI 线程的刷新可能在视图关掉之后才到

    [ObservableProperty]
    private string _goal = string.Empty;

    [ObservableProperty] private string _reminder = string.Empty; //只给化身看的重要提醒

    [ObservableProperty] private bool _infinite; //无限模式：只有用户手动能结束

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话（智能体群）</param>
    /// <param name="messages">结束离席前弹确认用的消息服务</param>
    /// <param name="away">离席控制器；null 取应用里的那一个</param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    public GroupAwayViewData(ChatSession group, IMessageService messages, GroupAwayController? away = null,
        Func<DateTimeOffset>? now = null)
    {
        _group = group;
        _messages = messages;
        _away = away ?? GroupAwayController.Instance;
        _now = now ?? (() => DateTimeOffset.Now);
        // 化身模型走会话模型组件的空态草稿：尚无化身会话，预选存在草稿里，开离席时带入
        ModelPicker = new SessionModelViewData(() => null, () => false, () => { });
        ModelPicker.Refresh();
        PreselectHostModel(group);
        _status = _away.StatusOf(group.SessionId);
        SyncAvatarUsage();
        _ticker.Tick += (_, _) => OnPropertyChanged(nameof(CountdownLine));
        _away.StatusChanged += OnStatusChanged;
        SyncTicker();
    }

    /// <summary>化身模型的选项（会话模型组件，空态草稿模式）</summary>
    public SessionModelViewData ModelPicker { get; }

    /// <summary>在离席</summary>
    public bool IsAway => _status != null;

    /// <summary>没在离席（弹窗里开始按钮的显隐）</summary>
    public bool IsIdle => _status == null;

    /// <summary>群的化身会话已建（化身标题点不点得开）；离席结束后会话还在，照样点得开</summary>
    public bool HasAvatar => _avatarUsage != null;

    /// <summary>化身的模型用量；化身会话还没建为 null</summary>
    public SessionUsageStats? AvatarUsage => _avatarUsage;

    /// <summary>化身调用过模型（标题下的用量行据此显隐）</summary>
    public bool HasAvatarUsage => _avatarUsage?.HasCost == true;

    /// <summary>化身正在跑（单行里转圈还是圆点）</summary>
    public bool IsAvatarRunning => _status?.IsAvatarRunning == true;

    /// <summary>捎话还能不能改：化身还没出过手（没产生聊天）就能改，改了随首轮投递</summary>
    public bool CanEditGoal => _status?.AvatarTurns is null or 0;

    /// <summary>弹窗参数与当前离席不一致（更新按钮据此置灰/点亮）</summary>
    public bool HasChanges => _status != null
        && (Goal != _status.Goal || Reminder != (_status.Reminder ?? string.Empty) || Infinite != _status.IsInfinite);

    /// <summary>单行圆点的配色键（status-dot 按 Tag 选色）：没在离席灰色，离席中蓝色</summary>
    public string DotTag => IsAway ? "Progress" : "Idle";

    /// <summary>单行里的状态行：没在离席是待机文案，离席中是出手次数或化身正在看群</summary>
    public string StatusLine => _status switch
    {
        null => Loc.Text(LangKey.GroupAwayIdle),
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
        ModelPicker.Dispose();
        _ticker.Stop();
    }

    [RelayCommand]
    private void Start()
    {
        // 空态草稿：没选过为 null，即跟随全局
        string? model = ModelPicker.PeekDraft();
        if (!_away.Start(_group, Goal, model, Reminder, Infinite)) return;
        Goal = string.Empty;
        Reminder = string.Empty;
        Infinite = false;
    }

    /// <summary>打开离席设置弹窗（捎话、提醒、模型、无限模式都搬了进去）；离席中先对齐当前实际参数</summary>
    [RelayCommand]
    private async Task OpenSetup()
    {
        SyncFromAway();
        await GroupAwaySetupWindow.ShowAsync(this);
    }

    /// <summary>
    /// 把弹窗输入对齐到正在进行的离席实际参数：开始后本地属性已清零，直接显示会误导
    /// （无限模式 toggle 尤其——它跟着的是会话里那份，不是本地这份）
    /// </summary>
    private void SyncFromAway()
    {
        if (_status is not { } status) return;
        Goal = status.Goal;
        Reminder = status.Reminder ?? string.Empty;
        Infinite = status.IsInfinite;
    }

    /// <summary>把弹窗里的捎话、提醒与无限模式写回正在进行的离席（提醒与无限模式下一轮生效）</summary>
    [RelayCommand]
    private void Update()
    {
        if (_status == null) return;
        if (!_away.Update(_group.SessionId, Goal, Reminder, Infinite)) return;
        // 更新后基准换成会话新值：变化清空，按钮回到置灰
        _status = _away.StatusOf(_group.SessionId);
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnGoalChanged(string value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnReminderChanged(string value) => OnPropertyChanged(nameof(HasChanges));
    partial void OnInfiniteChanged(bool value) => OnPropertyChanged(nameof(HasChanges));

    /// <summary>
    /// 某个会话刚报了一次用量（已在 UI 线程上）。是本群化身就刷它的用量行
    /// </summary>
    /// <param name="sessionId">报用量的会话</param>
    public void OnSessionUsageReported(string sessionId)
    {
        if (_disposed) return;
        if (_avatarUsage == null)
        {
            // 化身会话可能是这一拍才建出来（第一次离席）：实例还没建，补建并读数
            SyncAvatarUsage();
            return;
        }
        if (_avatarUsage.SessionId != sessionId) return;
        _avatarUsage.Refresh();
        OnPropertyChanged(nameof(HasAvatarUsage));
    }

    [RelayCommand]
    private void End() => _away.End(_group.SessionId);

    /// <summary>右栏停止按钮：确认后结束离席</summary>
    /// <returns>确认并结束了为 true；取消为 false（设置弹窗据此决定关窗时机）</returns>
    public async Task<bool> ConfirmEndAsync()
    {
        if (!await _messages.ConfirmAsync(Loc.Text(LangKey.GroupAwayEndConfirm))) return false;
        End();
        return true;
    }

    [RelayCommand]
    private Task ConfirmEnd() => ConfirmEndAsync();

    [RelayCommand]
    private void WakeNow() => _away.WakeNow(_group.SessionId);

    [RelayCommand]
    private void OpenAvatar()
    {
        // 离席结束后化身会话还在，照样点得开
        string? id = _status?.AvatarSessionId ?? _avatarUsage?.SessionId;
        if (id != null) SubSessionWindowOpener.Open(id);
    }

    // 默认用主持人的模型（没有主持人取第一位成员的）：化身做的是判断活，值得给群里最强的那个
    private void PreselectHostModel(ChatSession group)
    {
        string? memberId = group.GroupHostSessionId ?? group.GroupMemberSessionIds.FirstOrDefault();
        string? modelName = memberId == null ? null : SessionManager.Instance.GetMeta(memberId)?.SessionModelName;
        if (modelName == null) return;
        if (ModelPicker.Options.FirstOrDefault(x => !x.IsDefault && x.ModelName == modelName) is { } match)
            ModelPicker.SelectedOption = match;
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
            OnPropertyChanged(nameof(IsAvatarRunning));
            OnPropertyChanged(nameof(CanEditGoal));
            OnPropertyChanged(nameof(HasChanges));
            OnPropertyChanged(nameof(DotTag));
            OnPropertyChanged(nameof(StatusLine));
            OnPropertyChanged(nameof(CountdownLine));
            OnPropertyChanged(nameof(HasCountdown));
            SyncAvatarUsage();
            SyncTicker();
        });
    }

    // 化身会话在第一次离席时才建，之后跨离席保留：没建过的每次状态变化再找一次
    private void SyncAvatarUsage()
    {
        if (_avatarUsage == null)
        {
            string? id = _status?.AvatarSessionId ?? GroupAvatar.MetaOf(_group.SessionId)?.SessionId;
            if (id == null) return;
            _avatarUsage = new SessionUsageStats(id);
            OnPropertyChanged(nameof(HasAvatar));
            OnPropertyChanged(nameof(AvatarUsage));
        }

        _avatarUsage.Refresh();
        OnPropertyChanged(nameof(HasAvatarUsage));
    }

    private void SyncTicker()
    {
        if (HasCountdown) _ticker.Start();
        else _ticker.Stop();
    }
}
