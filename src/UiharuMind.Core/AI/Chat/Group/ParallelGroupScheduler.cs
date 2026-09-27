using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 并行（ADR 0049）：被叫醒的成员并发开跑；每条新发言按唤醒边界叫醒没在跑的人，
/// 正在跑的人经实时广播收到。没人在跑、也没人待叫醒，这一波就停（保守档的自动挂起就是这么来的）。
///
/// 在跑的人又被点到：广播已经把那句插给他，他这一轮的回应就算数（决策 7）；
/// 只有那句没被消费（他已在收尾）时，才在这一轮结束后再叫他一次。
/// </summary>
internal sealed class ParallelGroupScheduler : IGroupScheduler
{
    private readonly IGroupTurnHost _host;
    private readonly GroupRun _run;
    private readonly object _sync = new();
    private readonly Dictionary<string, EGroupWakeCause> _causes = new(); //在跑（或已排上）的成员 → 因何叫醒
    private readonly Dictionary<string, List<(int PostIndex, EGroupWakeCause Cause)>> _rewakes = new(); //在跑时又被点到
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active = 1; //开头那一份占位：初始唤醒全部排上之前，不许判定为空闲
    private bool _closed; //已判定空闲：这一波结束了，迟到的唤醒不再开跑

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="host">调度宿主</param>
    /// <param name="run">这一波</param>
    public ParallelGroupScheduler(IGroupTurnHost host, GroupRun run)
    {
        _host = host;
        _run = run;
    }

    public Task RunAsync(GroupKickoff kickoff)
    {
        IReadOnlyList<GroupWake> wakes = kickoff.UserPostIndex is { } index
            ? WakesForUserPost(_run.Group.History[index].Text)
            : _run.Group.GroupMemberSessionIds
                .Where(x => _host.HasNewLines(_run.Group, x))
                .Select(x => new GroupWake(x, EGroupWakeCause.User))
                .ToList();
        foreach (GroupWake wake in wakes) Wake(wake, kickoff.UserPostIndex ?? -1);

        Release();
        return _idle.Task;
    }

    public bool IsFinished
    {
        get
        {
            lock (_sync) return _closed;
        }
    }

    public bool OnPosted(GroupPostEvent post)
    {
        // 先占一份：算唤醒、叫醒人的这段时间里，这一波不许被判定为空闲——否则判完空闲才叫醒的人会被拒，这句就没人接
        lock (_sync)
        {
            if (_closed) return false;
            _active++;
        }

        try
        {
            IReadOnlyList<GroupWake> wakes = post.AuthorSessionId == null
                ? WakesForUserPost(post.Text)
                : WakesForMemberPost(post.AuthorSessionId, post.Text);
            foreach (GroupWake wake in wakes) Wake(wake, post.Index);
        }
        finally
        {
            Release();
        }

        return true;
    }

    private IReadOnlyList<GroupWake> WakesForMemberPost(string authorSessionId, string text)
    {
        EGroupWakeCause authorCause;
        lock (_sync) authorCause = _causes.GetValueOrDefault(authorSessionId, EGroupWakeCause.Member);
        return GroupWakePolicy.ForMemberPost(_run.Group.GroupMemberSessionIds, _run.Group.GroupHostSessionId,
            _run.StopPolicy, authorSessionId, authorCause, Mentions(text));
    }

    private IReadOnlyList<GroupWake> WakesForUserPost(string text) =>
        GroupWakePolicy.ForUserPost(_run.Group.GroupMemberSessionIds, _run.Group.GroupHostSessionId, Mentions(text));

    private IReadOnlyList<string> Mentions(string text) => GroupMentions.Parse(text, _host.RosterOf(_run.Group));

    private void Wake(GroupWake wake, int postIndex)
    {
        lock (_sync)
        {
            if (_closed || _run.Token.IsCancellationRequested) return;
            if (_causes.TryGetValue(wake.MemberSessionId, out EGroupWakeCause current))
            {
                // 跑着的时候被点到：那句会插进他这一轮，他接下来说的话按最近这一跳算——
                // 被成员点到之后再说的是第二跳，保守档下不再叫醒人；被主持人点名之后再说的按主持人叫醒算
                _causes[wake.MemberSessionId] = wake.Cause;

                if (!_rewakes.TryGetValue(wake.MemberSessionId, out var pending))
                {
                    pending = [];
                    _rewakes[wake.MemberSessionId] = pending;
                }

                pending.Add((postIndex, wake.Cause));
                return;
            }

            _causes[wake.MemberSessionId] = wake.Cause;
            _active++;
        }

        _ = LoopAsync(wake.MemberSessionId, wake.Cause);
    }

    private async Task LoopAsync(string memberId, EGroupWakeCause cause)
    {
        try
        {
            while (true)
            {
                GroupTurnOutcome outcome = await _host.RunMemberAsync(_run, memberId, cause).ConfigureAwait(false);
                lock (_sync)
                {
                    _rewakes.Remove(memberId, out var pending);
                    List<(int PostIndex, EGroupWakeCause Cause)> unmet = pending?
                        .Where(x => !outcome.ConsumedInjections.Contains(x.PostIndex))
                        .ToList() ?? [];
                    if (!outcome.Ran || unmet.Count == 0 || _run.Token.IsCancellationRequested)
                    {
                        _causes.Remove(memberId);
                        return;
                    }

                    cause = unmet.Min(x => x.Cause);
                    _causes[memberId] = cause;
                }
            }
        }
        catch (Exception e)
        {
            Log.Error($"Group member {memberId} failed in parallel run: {e}");
            lock (_sync)
            {
                _causes.Remove(memberId);
                _rewakes.Remove(memberId); //待补叫的一并丢：他这一轮已经没了，留着会让下次叫醒带上过期的来由
            }
        }
        finally
        {
            Release();
        }
    }

    private void Release()
    {
        bool idle;
        lock (_sync)
        {
            idle = --_active == 0;
            if (idle) _closed = true;
        }

        if (idle) _idle.TrySetResult();
    }
}
