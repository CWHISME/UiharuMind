using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群成员会话的轮次闸门（ADR 0046 未决「私聊与群轮交织」的落地）：
/// 群轮投递与用户私聊共用同一把闸，两轮永不重叠——它们共享会话本体、执行者与内容流，
/// 重叠就是 runner 释放/重建竞态（与子会话 <c>BackgroundSubAgentDispatcher._turnGates</c> 同理）。
///
/// 相遇策略（ADR 0063）：群轮抢闸，拿不到就跳过这一圈（游标不动，下一圈补投，不丢话）；
/// 私聊抢占——群轮占着闸时先排上队、再叫停它，等它停稳再开跑。私聊不插进群轮：群轮里说完的话都进群，私聊的回复会泄进去。
/// 被叫停的群轮经 <see cref="ResumeAfter"/> 约好私聊结束后接着做
/// </summary>
public static class GroupMemberTurnGate
{
    private static readonly ConcurrentDictionary<string, Gate> _gates = new();

    private static readonly IDisposable Noop = new NoopLease();

    /// <summary>
    /// 群轮抢闸，不让私聊抢占（私聊排队到它放闸）：成员正在私聊（闸被占用）返回 null
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <returns>闸的持有句柄；没抢到为 null</returns>
    public static IDisposable? TryEnter(string memberSessionId) => TryAcquire(memberSessionId, null);

    /// <summary>
    /// 群轮抢闸，持闸期间可被用户私聊叫停：成员正在私聊（闸被占用）返回 null，由调度器跳过这一圈
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="cancellationToken">这一轮原本的取消（用户停群等）</param>
    /// <returns>这一轮的持闸句柄；没抢到为 null</returns>
    public static PreemptibleTurn? TryEnterPreemptible(string memberSessionId, CancellationToken cancellationToken)
    {
        PreemptibleTurn turn = new(cancellationToken);
        IDisposable? lease = TryAcquire(memberSessionId, turn.Preempt);
        if (lease == null)
        {
            turn.Dispose();
            return null;
        }

        turn.Lease = lease;
        return turn;
    }

    /// <summary>
    /// 私聊等闸：群轮占着就先叫停它，再排队到它停稳。不是群成员会话立即放行（返回空操作）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">用户在等闸时停下这一轮</param>
    /// <returns>闸的持有句柄</returns>
    public static Task<IDisposable> EnterAsync(string? sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(sessionId)) return Task.FromResult(Noop);
        if (SessionManager.Instance.GetMeta(sessionId)?.IsGroupMember != true) return Task.FromResult(Noop);
        return EnterPrivateAsync(sessionId, cancellationToken);
    }

    // 不查会话头：测试的临时会话不在索引里
    internal static async Task<IDisposable> EnterPrivateAsync(string sessionId,
        CancellationToken cancellationToken = default)
    {
        Gate gate = GateOf(sessionId);
        Action? preempt;
        Task wait;
        // 先排上队再叫停：群轮一放闸就归私聊，不会被同时醒来的接回抢走
        lock (gate)
        {
            if (gate.Semaphore.Wait(0)) return new Lease(gate);
            preempt = gate.Preempt;
            wait = gate.Semaphore.WaitAsync(cancellationToken);
        }

        // 锁外叫停：取消会同步跑那一轮挂的回调
        preempt?.Invoke();
        await wait.ConfigureAwait(false);
        return new Lease(gate);
    }

    /// <summary>
    /// 约好「闸空了再接着做」：此刻没人持闸就当场做；否则等下一次放闸时做。
    /// 被私聊叫停的群轮用它接回——私聊可能一句接一句，到时若又被占着，由接回的那一方再约一次
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="resume">接着做（在线程池上跑，异常只记日志）</param>
    public static void ResumeAfter(string sessionId, Action resume)
    {
        Gate gate = GateOf(sessionId);
        lock (gate)
        {
            if (gate.Semaphore.CurrentCount == 0)
            {
                gate.Resume.Add(resume);
                return;
            }
        }

        Run(resume);
    }

    private static Gate GateOf(string sessionId) => _gates.GetOrAdd(sessionId, _ => new Gate());

    // 抢闸与登记叫停同在一把锁里：私聊不会在两步之间撞上「闸占着、却没人可叫停」
    private static IDisposable? TryAcquire(string sessionId, Action? onPreempt)
    {
        Gate gate = GateOf(sessionId);
        lock (gate)
        {
            if (!gate.Semaphore.Wait(0)) return null;
            gate.Preempt = onPreempt;
        }

        return new Lease(gate);
    }

    private static void Run(Action resume) => Task.Run(() =>
    {
        try
        {
            resume();
        }
        catch (Exception e)
        {
            Log.Error($"Group member resume failed: {e}");
        }
    });

    private sealed class Gate
    {
        public readonly SemaphoreSlim Semaphore = new(1, 1);
        public readonly List<Action> Resume = []; //放闸后要接着做的
        public Action? Preempt; //持闸的群轮怎么叫停；私聊持闸或不让抢占时为 null
    }

    private sealed class Lease : IDisposable
    {
        private readonly Gate _gate;
        private int _released;

        public Lease(Gate gate) => _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;

            List<Action> resume;
            lock (_gate)
            {
                _gate.Preempt = null;
                resume = [.._gate.Resume];
                _gate.Resume.Clear();
                _gate.Semaphore.Release();
            }

            foreach (Action action in resume) Run(action);
        }
    }

    private sealed class NoopLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>
/// 可被私聊叫停的一轮群轮的持闸句柄：这一轮用 <see cref="Token"/> 跑，收尾后据 <see cref="WasPreempted"/> 判断结局
/// </summary>
public sealed class PreemptibleTurn : IDisposable
{
    private readonly CancellationToken _parent;
    private readonly CancellationTokenSource _cancellation;
    private int _preempted;

    internal PreemptibleTurn(CancellationToken parent)
    {
        _parent = parent;
        _cancellation = CancellationTokenSource.CreateLinkedTokenSource(parent);
    }

    internal IDisposable? Lease { get; set; }

    /// <summary>这一轮跑的取消：原本的取消或私聊叫停</summary>
    public CancellationToken Token => _cancellation.Token;

    /// <summary>
    /// 这一轮是不是被私聊叫停的：没跑完、私聊叫过停，且原本的取消没来（同时停群按停群算）
    /// </summary>
    /// <param name="completed">这一轮是否正常跑完——跑完之后才到的叫停不算</param>
    /// <returns>是为 true</returns>
    public bool WasPreempted(bool completed) =>
        !completed && Volatile.Read(ref _preempted) == 1 && !_parent.IsCancellationRequested;

    internal void Preempt()
    {
        Interlocked.Exchange(ref _preempted, 1);
        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 那一轮恰好已收尾
        }
    }

    /// <summary>先放闸再丢取消源：放闸之后不会再有人来叫停</summary>
    public void Dispose()
    {
        Lease?.Dispose();
        _cancellation.Dispose();
    }
}
