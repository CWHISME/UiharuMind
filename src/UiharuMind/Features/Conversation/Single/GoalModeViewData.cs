using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Single.GoalMode;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Single;

/// <summary>
/// 目标模式设置弹窗里的一条调度规则编辑项：种类 + 参数，与 <see cref="GoalModeScheduleRule"/> 互转
/// </summary>
public sealed partial class GoalModeRuleEditorItem : ObservableObject
{
    [ObservableProperty] private EGoalScheduleKind _kind = EGoalScheduleKind.Interval;

    [ObservableProperty] private TimeOnly _dailyTime = new(9, 0); //Daily 的时间

    [ObservableProperty] private double _intervalMinutes = 30; //Interval 的间隔分钟数

    private DateTimeOffset _fireAt = DateTimeOffset.Now.AddDays(1); //OneShot 的触发时刻（经 FireAt 编辑）

    /// <summary>按种类切换参数编辑区的显隐</summary>
    public bool IsDaily => Kind == EGoalScheduleKind.Daily;

    /// <summary>按种类切换参数编辑区的显隐</summary>
    public bool IsOneShot => Kind == EGoalScheduleKind.OneShot;

    /// <summary>按种类切换参数编辑区的显隐</summary>
    public bool IsInterval => Kind == EGoalScheduleKind.Interval;

    /// <summary>OneShot 的触发时刻（Ursa DateTimePicker 用；本地时间语义）</summary>
    public DateTime? FireAt
    {
        get => _fireAt.LocalDateTime;
        set
        {
            if (value is not { } dt) return;
            _fireAt = new DateTimeOffset(dt, TimeZoneInfo.Local.GetUtcOffset(dt));
            OnPropertyChanged();
            OnPropertyChanged(nameof(Summary));
        }
    }

    /// <summary>显示文本</summary>
    public string Summary => Kind switch
    {
        EGoalScheduleKind.Daily => Loc.Text(LangKey.GoalModeRuleDailyFormat, DailyTime.ToString("HH:mm")),
        EGoalScheduleKind.OneShot => Loc.Text(LangKey.GoalModeRuleOneShotFormat, _fireAt.ToString("yyyy-MM-dd HH:mm")),
        EGoalScheduleKind.Immediate => Loc.Text(LangKey.GoalModeRuleImmediate),
        _ => Loc.Text(LangKey.GoalModeRuleIntervalFormat, IntervalMinutes.ToString("0")),
    };

    /// <summary>从控制器的规则建编辑项（打开弹窗时对齐正在进行的规则）</summary>
    /// <param name="rule">规则</param>
    /// <returns>编辑项</returns>
    public static GoalModeRuleEditorItem From(GoalModeScheduleRule rule)
    {
        GoalModeRuleEditorItem item = new() { Kind = rule.Kind };
        if (rule.DailyTime is { } time) item.DailyTime = time;
        if (rule.Interval is { } interval) item.IntervalMinutes = interval.TotalMinutes;
        if (rule.FireAt is { } fireAt) item._fireAt = fireAt;
        return item;
    }

    /// <summary>转成控制器认的规则</summary>
    /// <returns>规则</returns>
    public GoalModeScheduleRule ToRule() => Kind switch
    {
        EGoalScheduleKind.Daily => new GoalModeScheduleRule(Kind, DailyTime: DailyTime),
        EGoalScheduleKind.OneShot => new GoalModeScheduleRule(Kind, FireAt: _fireAt),
        EGoalScheduleKind.Interval => new GoalModeScheduleRule(Kind, Interval: TimeSpan.FromMinutes(Math.Max(1, IntervalMinutes))),
        _ => new GoalModeScheduleRule(Kind),
    };

    partial void OnKindChanged(EGoalScheduleKind value)
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(IsDaily));
        OnPropertyChanged(nameof(IsOneShot));
        OnPropertyChanged(nameof(IsInterval));
    }

    partial void OnDailyTimeChanged(TimeOnly value) => OnPropertyChanged(nameof(Summary));
    partial void OnIntervalMinutesChanged(double value) => OnPropertyChanged(nameof(Summary));
}

/// <summary>
/// 单聊目标模式的视图数据（设置弹窗的数据上下文）：目标文本 + 调度规则增删，
/// 状态跟着 <see cref="GoalModeController.StatusChanged"/> 刷新
/// </summary>
public sealed partial class GoalModeViewData : WakeLoopViewData
{
    private readonly ChatSession _session;
    private readonly GoalModeController _goalMode;
    private GoalModeStatus? _status;

    [ObservableProperty] private string _goal = string.Empty;

    [ObservableProperty] private bool _autoApproveApprovals;

    private bool _syncingStatus; //从控制器回填期间不当作用户勾选：别弹确认

    /// <summary>构造</summary>
    /// <param name="session">会话（agent 档）</param>
    /// <param name="messages">结束前弹确认用的消息服务</param>
    /// <param name="goalMode">目标模式控制器；null 取应用里的那一个</param>
    /// <param name="now">当前时刻；null 取本地时间</param>
    public GoalModeViewData(ChatSession session, IMessageService messages, GoalModeController? goalMode = null,
        Func<DateTimeOffset>? now = null) : base(session.SessionId, messages, now)
    {
        _session = session;
        _goalMode = goalMode ?? GoalModeController.Instance;
        _status = _goalMode.StatusOf(session.SessionId);
        Rules.CollectionChanged += OnRulesChanged;
        _goalMode.StatusChanged += OnStatusChanged;
        SyncFromGoalMode();
        SyncTicker();
    }

    /// <summary>调度规则编辑项（开始/更新时转回控制器认的规则）</summary>
    public ObservableCollection<GoalModeRuleEditorItem> Rules { get; } = [];

    /// <summary>在目标模式</summary>
    public override bool IsActive => _status != null;

    /// <summary>本实例管不管目标模式：先启动的实例才管（ADR 0064），否则开始按钮置灰并说明</summary>
    public bool IsOwner => _goalMode.IsOwner;

    /// <summary>模型正在跑</summary>
    public bool IsRunning => _status?.IsRunning == true;

    /// <summary>能立即唤醒：在目标模式且没在跑（没有可触发的规则时这是唯一的唤醒入口）</summary>
    public bool CanWakeNow => _status is { IsRunning: false };

    /// <summary>状态行：没在是待机文案，在时是已跑轮数或正在跑</summary>
    public string StatusLine => _status switch
    {
        null => Loc.Text(LangKey.GoalModeIdle),
        { IsRunning: true } => Loc.Text(LangKey.GoalModeThinking),
        _ => Loc.Text(LangKey.GoalModeStatusFormat, _status.Turns),
    };

    /// <inheritdoc />
    public override string CountdownLine => CountdownOf(LangKey.GoalModeCountdownFormat);

    /// <summary>弹窗里的目标或规则与正在进行的不一致（更新按钮据此置灰/点亮）</summary>
    public bool HasChanges => _status != null
        && (Goal.Trim() != _status.Goal || !EditedRules().SequenceEqual(_status.Rules)
            || AutoApproveApprovals != _status.AutoApproveApprovals);

    /// <inheritdoc />
    protected override DateTimeOffset? WakeAt => _status?.WakeAt;

    /// <inheritdoc />
    protected override LangKey EndConfirmKey => LangKey.GoalModeEndConfirm;

    /// <summary>摘掉订阅、停掉倒计时</summary>
    public override void Dispose()
    {
        _goalMode.StatusChanged -= OnStatusChanged;
        base.Dispose();
    }

    /// <summary>开始目标模式：交规则（空则控制器用默认循环）</summary>
    [RelayCommand]
    private void Start() => _goalMode.Start(_session, Settings());

    /// <summary>更新正在进行的目标模式：目标下一轮起生效，规则立即重排</summary>
    [RelayCommand]
    private void Update()
    {
        if (!_goalMode.Update(_session.SessionId, Settings())) return;
        _status = _goalMode.StatusOf(_session.SessionId);
        SyncFromGoalMode(); //空规则被控制器换成默认循环：编辑区跟着显示实际生效的那条
    }

    /// <summary>加一条调度规则（种类为下拉框选中的序号，与 <see cref="EGoalScheduleKind"/> 同序）</summary>
    /// <param name="index">选中的序号</param>
    [RelayCommand]
    private void AddRule(int index) => Rules.Add(new GoalModeRuleEditorItem { Kind = (EGoalScheduleKind)index });

    /// <summary>删一条调度规则</summary>
    /// <param name="item">编辑项</param>
    [RelayCommand]
    private void RemoveRule(GoalModeRuleEditorItem item) => Rules.Remove(item);

    /// <inheritdoc />
    protected override void EndCore() => _goalMode.End(_session.SessionId);

    /// <inheritdoc />
    protected override void WakeNowCore() => _goalMode.WakeNow(_session.SessionId);

    /// <inheritdoc />
    protected override void ReloadStatus() => _status = _goalMode.StatusOf(_session.SessionId);

    /// <inheritdoc />
    protected override void OnStatusReloaded()
    {
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(CanWakeNow));
        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(HasChanges));
    }

    partial void OnGoalChanged(string value) => OnPropertyChanged(nameof(HasChanges));

    // 勾上「自动同意审批」先二次确认：开了就是无人值守也直接放行，危险度得当面说清。取消就不勾上
    async partial void OnAutoApproveApprovalsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasChanges));
        if (_syncingStatus || !value) return;
        if (await Messages.ConfirmAsync(Loc.Text(LangKey.GoalModeAutoApproveConfirm),
                Loc.Text(LangKey.GoalModeAutoApproveConfirmTitle)))
        {
            return;
        }

        // 回拨放到下一趟调度：勾选是绑定推过来的，紧接着原路写回会被它当成同一次变更的回环吃掉，
        // 界面上那个勾就留着不退
        Dispatcher.UIThread.Post(() => AutoApproveApprovals = false);
    }

    // 弹窗对齐到正在进行的实际参数；没在目标模式就保留当前输入
    private void SyncFromGoalMode()
    {
        if (_status is not { } status) return;
        _syncingStatus = true;
        Goal = status.Goal;
        AutoApproveApprovals = status.AutoApproveApprovals;
        _syncingStatus = false;
        foreach (GoalModeRuleEditorItem item in Rules) item.PropertyChanged -= OnRuleEdited; //Clear 的 Reset 不带旧项
        Rules.Clear();
        foreach (GoalModeScheduleRule rule in status.Rules) Rules.Add(GoalModeRuleEditorItem.From(rule));
        OnPropertyChanged(nameof(HasChanges));
    }

    private GoalModeScheduleRule[] EditedRules() => Rules.Select(x => x.ToRule()).ToArray();

    // 弹窗里此刻的几样参数:开始与更新走同一份,别在两处各拼一遍
    private GoalModeSettings Settings() => new(Goal, EditedRules(), AutoApproveApprovals);

    // 增删规则、改规则参数都要刷新「有没有改动」
    private void OnRulesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (GoalModeRuleEditorItem item in e.OldItems) item.PropertyChanged -= OnRuleEdited;
        }

        if (e.NewItems != null)
        {
            foreach (GoalModeRuleEditorItem item in e.NewItems) item.PropertyChanged += OnRuleEdited;
        }

        OnPropertyChanged(nameof(HasChanges));
    }

    private void OnRuleEdited(object? sender, PropertyChangedEventArgs e) => OnPropertyChanged(nameof(HasChanges));
}
