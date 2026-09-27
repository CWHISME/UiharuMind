namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一个群正在跑的「一波」：从用户发言（或点「继续」）开始，到没人再在跑为止。
/// 模式与停止条件在开波时定格——运行中切换，下一波起生效（ADR 0049 决策 1）
/// </summary>
internal sealed class GroupRun : IDisposable
{
    private readonly object _sync = new();
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Dictionary<string, GroupMemberTurnState> _turns = new(); //成员会话 → 正在跑的那一轮
    private Task _broadcastTail = Task.CompletedTask; //广播按追加顺序排队，同一人收到的插话不乱序

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="group">群壳会话</param>
    public GroupRun(ChatSession group)
    {
        Group = group;
        Mode = group.GroupScheduleMode;
        StopPolicy = group.GroupStopPolicy;
    }

    /// <summary>群壳会话</summary>
    public ChatSession Group { get; }

    /// <summary>这一波的调度模式</summary>
    public EGroupScheduleMode Mode { get; }

    /// <summary>这一波的停止条件</summary>
    public EGroupStopPolicy StopPolicy { get; }

    /// <summary>用户停止时取消</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>此刻正在发言的成员会话，按发言顺序</summary>
    public IReadOnlyList<string> Speakers
    {
        get
        {
            lock (_sync) return Group.GroupMemberSessionIds.Where(_turns.ContainsKey).ToList();
        }
    }

    /// <summary>此刻正在跑的各轮</summary>
    public IReadOnlyList<GroupMemberTurnState> Turns
    {
        get
        {
            lock (_sync) return _turns.Values.ToList();
        }
    }

    /// <summary>停下这一波</summary>
    public void Cancel() => _cancellation.Cancel();

    /// <summary>登记一位成员开始发言</summary>
    /// <param name="turn">他这一轮</param>
    public void Begin(GroupMemberTurnState turn)
    {
        lock (_sync) _turns[turn.Member.SessionId] = turn;
    }

    /// <summary>一位成员这一轮结束</summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <returns>他确实在发言为 true</returns>
    public bool End(string memberSessionId)
    {
        lock (_sync) return _turns.Remove(memberSessionId);
    }

    /// <summary>
    /// 排一次广播：前一次广播做完才开始这一次
    /// </summary>
    /// <param name="broadcast">广播本体</param>
    /// <returns>这一次广播做完</returns>
    public Task EnqueueBroadcast(Func<Task> broadcast)
    {
        lock (_sync)
        {
            _broadcastTail = _broadcastTail.ContinueWith(_ => broadcast(), CancellationToken.None,
                TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();
            return _broadcastTail;
        }
    }

    public void Dispose() => _cancellation.Dispose();
}
