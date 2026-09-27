namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 串行（ADR 0046 决策 5）：成员按顺序一人一轮，没有新话可接的跳过，一圈即停。
/// 不认 @、不看主持人——用户插话照样经广播插进当前发言人那一轮
/// </summary>
internal sealed class SerialGroupScheduler : IGroupScheduler
{
    private readonly IGroupTurnHost _host;
    private readonly GroupRun _run;

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

    public async Task RunAsync(GroupKickoff kickoff)
    {
        foreach (string memberId in _run.Group.GroupMemberSessionIds.ToList())
        {
            if (_run.Token.IsCancellationRequested) break;
            await _host.RunMemberAsync(_run, memberId, EGroupWakeCause.User);
        }
    }

    public void OnPosted(GroupPostEvent post)
    {
    }
}
