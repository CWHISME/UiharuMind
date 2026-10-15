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

    /// <summary>延迟到点时认领：这次延迟没被取消、循环也没结束，就摘掉它交给调用方去唤醒</summary>
    /// <param name="token">那次延迟的令牌</param>
    /// <returns>该唤醒为 true</returns>
    public bool TryTakeDue(CancellationToken token)
    {
        if (token.IsCancellationRequested || IsEnded) return false;
        CancelDelay();
        return true;
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
