namespace UiharuMind.Core.AI.Chat.WakeLoops;

/// <summary>
/// 后台唤醒循环（离席、目标模式）一次会话的公共状态：生命周期令牌、延迟唤醒、进展基线与结束原因。
/// 没有自己的锁：一律在控制器的锁里读写
/// </summary>
/// <typeparam name="TReason">结束原因</typeparam>
internal abstract class WakeLoopSession<TReason> where TReason : struct, Enum
{
    private readonly CancellationTokenSource _lifetime = new(); //结束即取消：正在跑的那一轮随之停下
    private CancellationTokenSource? _delay; //正在等的延迟唤醒

    /// <summary>开一次循环</summary>
    /// <param name="startedAt">开始时刻</param>
    /// <param name="artifactStamp">开始时的产物指纹</param>
    protected WakeLoopSession(DateTimeOffset startedAt, string artifactStamp)
    {
        StartedAt = startedAt;
        ArtifactStamp = artifactStamp;
    }

    /// <summary>开始时刻</summary>
    public DateTimeOffset StartedAt { get; }

    /// <summary>最近一次看到的产物指纹（进展判据的基线）</summary>
    public string ArtifactStamp { get; set; }

    /// <summary>连续没进展的次数（退避的级数）</summary>
    public int BackoffLevel { get; set; }

    /// <summary>被唤醒的那一轮正在跑</summary>
    public bool IsRunning { get; set; }

    /// <summary>延迟唤醒的到点时刻；没在等为 null</summary>
    public DateTimeOffset? WakeAt { get; private set; }

    /// <summary>已结束（回执可能还要等那一轮停稳才出）</summary>
    public bool IsEnded { get; private set; }

    /// <summary>结束原因；没结束为 null</summary>
    public TReason? EndReason { get; private set; }

    /// <summary>模型的交代；没有为空</summary>
    public string EndSummary { get; private set; } = string.Empty;

    /// <summary>生命周期：结束即取消</summary>
    public CancellationToken Token => _lifetime.Token;

    /// <summary>排上一次延迟唤醒（先取消正在等的那一次）</summary>
    /// <param name="wakeAt">到点时刻</param>
    /// <returns>取消这次延迟用的令牌</returns>
    public CancellationToken BeginDelay(DateTimeOffset wakeAt)
    {
        CancelDelay();
        _delay = new CancellationTokenSource();
        WakeAt = wakeAt;
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
        WakeAt = null;
        return true;
    }

    /// <summary>
    /// 到点了，认领这次唤醒。<b>参加唤醒的入口都问这一处，别在控制器里各拼一套忙闲判断</b>：
    /// 那次等待还有效、循环没结束、会话此刻也不忙，才是 <see cref="EWakeDue.Go"/>。
    ///
    /// 认领不到时那条等待一并摘掉，这一次唤醒<b>不算发生</b>：不注入、不计轮，由控制器在自己的
    /// 收场信号上补——单聊记成「欠一次」等那一轮收场（见 <c>GoalModeController</c>），
    /// 群离席在波末重判（见 <c>GroupAwayController</c>）。
    /// </summary>
    /// <param name="token">那次等待的令牌</param>
    /// <param name="busy">会话此刻被<b>别的</b>轮次占着（用户在跑自己那一轮）；本循环自己那一轮由调用方另判。
    /// <b>具体含什么由调用方按自己的业务边界定，闸不替你判</b>：单聊付的是
    /// <c>IsBusy &amp;&amp; !IsRunning</c>（排除本循环自己那一轮），群离席付的是「群在跑一波」（不含化身自己那一轮）</param>
    /// <returns>处置结论</returns>
    public EWakeDue ClaimDue(CancellationToken token, bool busy)
    {
        if (token.IsCancellationRequested || IsEnded) return EWakeDue.Cancelled;
        CancelDelay();
        return busy ? EWakeDue.Busy : EWakeDue.Go;
    }

    /// <summary>结束：取消延迟与正在跑的那一轮</summary>
    /// <param name="reason">原因</param>
    /// <param name="summary">模型的交代</param>
    public void End(TReason reason, string summary)
    {
        IsEnded = true;
        EndReason = reason;
        EndSummary = summary;
        CancelDelay();
        _lifetime.Cancel();
    }

    /// <summary>回执交出之后释放：此后谁也不再取 <see cref="Token"/></summary>
    public void Dispose() => _lifetime.Dispose();
}
