using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 对框架注入队列（<see cref="MessageInjectingChatClient"/> 存在 <c>AgentSession.StateBag</c> 里的那份）
/// 的有限访问。
///
/// 框架只给了 <c>EnqueueMessagesAsync</c> / <c>GetPendingMessagesAsync</c> 两个口子，没有「退订单条」；
/// 撤回插话必须把那条消息从队列里摘掉，所以这里反射碰到了框架内部。两处私有：
/// 队列存放的 key 与守护队列的那把 per-session 锁。
/// 锁必须拿**同一把**——<c>DrainInjectedMessagesAsync</c> 在服务调用循环里清队列，
/// 各拿各的锁会把「取走」与「撤回」交错成丢消息。
///
/// 升级框架时若 <c>_sessionLocks</c> 字段改名，这里在类型加载期就抛（字段反射结果缓存为静态，
/// 构造函数抛 => TypeInitializationException），调用方要按「撒不回队列」兜底——见
/// <see cref="HarnessCharacterRunner.CancelInjectionsAsync"/> 的容错注释。
/// </summary>
internal static class PendingInjectionQueueAccess
{
    /// <summary>框架把注入队列存进 StateBag 用的 key（internal const，值稳定，见框架源码）</summary>
    private const string PendingMessagesStateKey = "MessageInjectingChatClient.PendingInjectedMessages";

    private static readonly FieldInfo SessionLocksField = typeof(MessageInjectingChatClient).GetField(
        "_sessionLocks", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException(
            "MessageInjectingChatClient._sessionLocks 字段缺失——框架私有成员变了，撤回插话的通道需要重写。");

    /// <summary>
    /// 从注入队列移除目标消息。
    /// </summary>
    /// <param name="injector">注入器（取自 <see cref="AgentHandle.MessageInjector"/>）</param>
    /// <param name="session">注入队列归属的框架会话</param>
    /// <param name="target">要撤回的那条插话</param>
    /// <returns>true 表示当时还在队列里并已摘走；false 表示已不在队列（已被消费、或根本没进）</returns>
    public static async Task<bool> RemoveAsync(MessageInjectingChatClient injector, AgentSession session,
        ChatMessage target, CancellationToken cancellationToken = default)
    {
        var locks = (ConditionalWeakTable<AgentSession, SemaphoreSlim>)SessionLocksField.GetValue(injector)!;
        SemaphoreSlim gate = locks.GetValue(session, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!session.StateBag.TryGetValue<List<ChatMessage>>(PendingMessagesStateKey, out List<ChatMessage>? queue)
                || queue is null)
            {
                return false;
            }

            return queue.RemoveAll(m => ReferenceEquals(m, target)) > 0;
        }
        finally
        {
            gate.Release();
        }
    }
}