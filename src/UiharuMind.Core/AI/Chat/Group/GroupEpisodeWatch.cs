using System.Threading.Channels;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一次「等这个群这一波跑完」的订阅。
///
/// <b>必须在发帖之前开始</b>：<see cref="GroupChatCoordinator.PostAsync"/> 的同步段就可能把整波跑完
/// （群里没有可叫醒的成员时调度器直接返回），事后再订阅就永远等不到那一次 EpisodeEnded。
///
/// 收场按队列排队：订阅之后接连收场几波也不会丢，「这一波不含我这句」时能接着取下一波。
/// 用完必须 <see cref="Dispose"/> 退订——所有权归接手的等待任务。
/// </summary>
internal sealed class GroupEpisodeWatch : IDisposable
{
    private readonly GroupChatCoordinator _coordinator;
    private readonly string _groupId;
    private readonly Action<GroupEpisodeSummary> _handler;
    private readonly Channel<GroupEpisodeSummary> _ended = Channel.CreateUnbounded<GroupEpisodeSummary>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private int _disposed;

    private GroupEpisodeWatch(GroupChatCoordinator coordinator, string groupId)
    {
        _coordinator = coordinator;
        _groupId = groupId;
        _handler = OnEnded;
    }

    /// <summary>订阅某个群的一波收场（调用方负责 <see cref="Dispose"/>）</summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>订阅</returns>
    public static GroupEpisodeWatch Begin(string groupId) => Begin(GroupChatCoordinator.Instance, groupId);

    /// <summary>订阅某个群的一波收场（测试用自建的调度器）</summary>
    /// <param name="coordinator">调度器</param>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>订阅</returns>
    internal static GroupEpisodeWatch Begin(GroupChatCoordinator coordinator, string groupId)
    {
        GroupEpisodeWatch watch = new(coordinator, groupId);
        coordinator.EpisodeEnded += watch._handler;
        return watch;
    }

    /// <summary>取下一次收场；订阅之后已经收场过的话，这里立刻拿得到</summary>
    /// <param name="cancellationToken">取消等待</param>
    /// <returns>收场回顾</returns>
    public Task<GroupEpisodeSummary> NextAsync(CancellationToken cancellationToken) =>
        _ended.Reader.ReadAsync(cancellationToken).AsTask();

    /// <summary>退订</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _coordinator.EpisodeEnded -= _handler;
        _ended.Writer.TryComplete();
    }

    private void OnEnded(GroupEpisodeSummary summary)
    {
        if (summary.Group.SessionId != _groupId) return;
        _ended.Writer.TryWrite(summary);
    }
}
