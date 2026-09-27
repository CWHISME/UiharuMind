namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 串行（ADR 0046 决策 5）：成员按顺序一人一轮，没有新话可接的跳过，一圈即停。
/// 不认 @、不看主持人——用户插话照样经广播插进当前发言人那一轮
/// </summary>
internal sealed class SerialGroupScheduler : IGroupScheduler
{
    private readonly IGroupTurnHost _host;
    private readonly GroupRun _run;
    private readonly object _sync = new();
    private bool _finished; //一圈跑完：之后来的发言由宿主另开一圈

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="host">调度宿主</param>
    /// <param name="run">这一波</param>
    public SerialGroupScheduler(IGroupTurnHost host, GroupRun run)
    {
        _host = host;
        _run = run;
    }

    public bool IsFinished
    {
        get
        {
            lock (_sync) return _finished;
        }
    }

    public async Task RunAsync(GroupKickoff kickoff)
    {
        try
        {
            foreach (string memberId in _run.Group.GroupMemberSessionIds.ToList())
            {
                if (_run.Token.IsCancellationRequested) break;
                await _host.RunMemberAsync(_run, memberId, EGroupWakeCause.User);
            }
        }
        finally
        {
            lock (_sync) _finished = true;
        }
    }

    // 已知限制：最后一位正在收尾时来的插话，若没赶上被他消费，这一圈不再有人接，等下次「继续」按游标补投。
    // 别用「广播后再查一次收没收场」去补：插进最后一轮、那轮随后收尾的会被误判成没接住，重开一圈就说两遍
    public bool OnPosted(GroupPostEvent post)
    {
        lock (_sync) return !_finished;
    }
}
