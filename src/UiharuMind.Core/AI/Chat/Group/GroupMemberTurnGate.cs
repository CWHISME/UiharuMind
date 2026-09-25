using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 群成员会话的轮次闸门（ADR 0046 未决「私聊与群轮交织」的落地）：
/// 群轮投递与用户私聊共用同一把闸，两轮永不重叠——它们共享会话本体、执行者与内容流，
/// 重叠就是 runner 释放/重建竞态（与子会话 <c>BackgroundSubAgentDispatcher._turnGates</c> 同理）。
///
/// 相遇策略：群轮抢闸，拿不到就跳过这一圈（游标不动，下一圈补投，不丢话）；
/// 私聊等闸，排队到群轮结束再开跑。
/// </summary>
public static class GroupMemberTurnGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new();

    private static readonly IDisposable Noop = new NoopLease();

    /// <summary>
    /// 群轮抢闸：成员正在私聊（闸被占用）返回 null，由调度器跳过这一圈
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <returns>闸的持有句柄；没抢到为 null</returns>
    public static IDisposable? TryEnter(string memberSessionId)
    {
        SemaphoreSlim gate = _gates.GetOrAdd(memberSessionId, _ => new SemaphoreSlim(1, 1));
        return gate.Wait(0) ? new Lease(gate) : null;
    }

    /// <summary>
    /// 私聊等闸：排队到群轮结束（它整轮持有闸）。不是群成员会话立即放行（返回空操作）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>闸的持有句柄</returns>
    public static async Task<IDisposable> EnterAsync(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return Noop;
        if (SessionManager.Instance.GetMeta(sessionId)?.IsGroupMember != true) return Noop;

        SemaphoreSlim gate = _gates.GetOrAdd(sessionId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease : IDisposable
    {
        private readonly SemaphoreSlim _gate;
        private int _released;

        public Lease(SemaphoreSlim gate) => _gate = gate;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) _gate.Release();
        }
    }

    private sealed class NoopLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
