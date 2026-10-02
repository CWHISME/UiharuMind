namespace UiharuMind.Core.AI.Chat.Group.Away;

/// <summary>
/// 一次离席的可变状态（ADR 0055）。没有自己的锁：一律在 <see cref="GroupAwayController"/> 的锁里读写
/// </summary>
internal sealed class GroupAwaySession
{
    private readonly CancellationTokenSource _lifetime = new(); //离席结束即取消：化身正在跑的那一轮随之停下
    private CancellationTokenSource? _delay; //正在等的延迟唤醒
    private DateTimeOffset? _wakeAt;
    private string? _note; //带给化身下一轮的提示（上次没进展的情况），交出即清

    /// <summary>
    /// 开一次离席
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="avatar">化身会话</param>
    /// <param name="settings">定格的离席参数</param>
    /// <param name="startedAt">开始时刻</param>
    /// <param name="artifactStamp">开始时的本群产物指纹</param>
    /// <param name="mandate">只给化身看的授权范围；没有为 null</param>
    public GroupAwaySession(ChatSession group, ChatSession avatar, GroupAwaySettings settings, DateTimeOffset startedAt,
        string artifactStamp, string? mandate = null)
    {
        Mandate = string.IsNullOrWhiteSpace(mandate) ? null : mandate.Trim();
        Group = group;
        Avatar = avatar;
        Settings = settings;
        StartedAt = startedAt;
        ArtifactStamp = artifactStamp;
        HistoryStart = group.History.Count;
        AvatarHistoryStart = avatar.History.Count;
    }

    /// <summary>群壳会话</summary>
    public ChatSession Group { get; }

    /// <summary>化身会话</summary>
    public ChatSession Avatar { get; }

    /// <summary>只给化身看的授权范围；没有为 null</summary>
    public string? Mandate { get; }

    /// <summary>定格的离席参数</summary>
    public GroupAwaySettings Settings { get; }

    /// <summary>开始时刻</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>开始时的群流水长度：回执只认这之后化身说的话</summary>
    public int HistoryStart { get; }

    /// <summary>开始时化身历史的长度：回执只认这之后它自己动手做的事</summary>
    public int AvatarHistoryStart { get; }

    /// <summary>最近一次看到的本群产物指纹</summary>
    public string ArtifactStamp { get; set; }

    /// <summary>化身点过的审批</summary>
    public List<GroupAwayApproval> Approvals { get; } = [];

    /// <summary>化身已出手几次</summary>
    public int AvatarTurns { get; set; }

    /// <summary>连续没进展的次数（退避的级数）</summary>
    public int BackoffLevel { get; set; }

    /// <summary>连续没有新产物的波数</summary>
    public int IdleWaves { get; set; }

    /// <summary>化身正在跑</summary>
    public bool IsAvatarRunning { get; set; }

    /// <summary>化身跑着时又有一波收场：跑完再叫</summary>
    public bool WakePending { get; set; }

    /// <summary>已结束（回执可能还要等化身那一轮停稳才出）</summary>
    public bool IsEnded { get; private set; }

    /// <summary>结束原因；没结束为 null</summary>
    public EGroupAwayEndReason? EndReason { get; private set; }

    /// <summary>化身的交代；保险丝与手动结束为空</summary>
    public string EndSummary { get; private set; } = string.Empty;

    /// <summary>离席的生命周期：结束即取消</summary>
    public CancellationToken Token => _lifetime.Token;

    /// <summary>
    /// 排上一次延迟唤醒（调用方先 <see cref="CancelDelay"/>）
    /// </summary>
    /// <param name="wakeAt">到点时刻</param>
    /// <returns>取消这次延迟用的令牌</returns>
    public CancellationToken BeginDelay(DateTimeOffset wakeAt)
    {
        _delay = new CancellationTokenSource();
        _wakeAt = wakeAt;
        return _delay.Token;
    }

    /// <summary>取消正在等的延迟唤醒（没有就什么也不做）</summary>
    /// <returns>确实取消了一次为 true</returns>
    public bool CancelDelay()
    {
        if (_delay == null) return false;
        _delay.Cancel();
        _delay.Dispose();
        _delay = null;
        _wakeAt = null;
        return true;
    }

    /// <summary>
    /// 记下带给化身下一轮的提示：新的情况盖掉旧的，没有新情况时留着旧的
    /// </summary>
    /// <param name="note">提示；null 不改</param>
    public void CarryNote(string? note)
    {
        if (note != null) _note = note;
    }

    /// <summary>取走带给化身这一轮的提示</summary>
    /// <returns>提示；没有为 null</returns>
    public string? TakeNote()
    {
        string? note = _note;
        _note = null;
        return note;
    }

    /// <summary>
    /// 结束：取消延迟与化身正在跑的那一轮
    /// </summary>
    /// <param name="reason">原因</param>
    /// <param name="summary">化身的交代</param>
    public void End(EGroupAwayEndReason reason, string summary)
    {
        IsEnded = true;
        EndReason = reason;
        EndSummary = summary;
        CancelDelay();
        _lifetime.Cancel();
    }

    /// <summary>回执交出之后释放：此后谁也不再取 <see cref="Token"/></summary>
    public void Dispose() => _lifetime.Dispose();

    /// <summary>界面用的快照</summary>
    /// <returns>状态</returns>
    public GroupAwayStatus Snapshot() =>
        new(Group.SessionId, Avatar.SessionId, StartedAt, AvatarTurns, IsAvatarRunning, _wakeAt);
}
