using Microsoft.Extensions.AI;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 一位成员正在跑的那一轮：从投递那一刻起接收插话，收尾时把没被消费的撤回来。
///
/// 插话与收尾共用一把异步闸，是为了堵「收尾之后才入队」的缝：那条会留在队列里，
/// 到他下一轮第一次调用才被取走——晚到，而且紧跟在新投递后面，成了连续两条 user 消息。
/// </summary>
internal sealed class GroupMemberTurnState
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<(int Index, ChatMessage Message)> _injected = []; //这一轮插进去的群流水下标与消息
    private readonly List<(ChatMessage Message, Action<bool> Settle)> _notes = []; //这一轮插进去的附注与它的结局回调
    private bool _accepting = true;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="member">成员会话</param>
    /// <param name="cause">这一轮因何被叫醒</param>
    /// <param name="cursor">投递时的群流水长度</param>
    public GroupMemberTurnState(ChatSession member, EGroupWakeCause cause, int cursor)
    {
        Member = member;
        Cause = cause;
        Cursor = cursor;
    }

    /// <summary>成员会话</summary>
    public ChatSession Member { get; }

    /// <summary>这一轮因何被叫醒</summary>
    public EGroupWakeCause Cause { get; }

    /// <summary>投递时的群流水长度：之前的都随投递交过了，之后的才需要插话</summary>
    public int Cursor { get; }

    /// <summary>
    /// 把一条群发言插进这一轮。已收尾、或它本来就在投递里的，不插
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    /// <param name="index">群流水下标</param>
    /// <param name="text">插话正文（已带发言人前缀）</param>
    /// <param name="images">转交给他的图片；他看不了图时传空</param>
    public async Task InjectAsync(IGroupMemberTurnRunner runner, int index, string text,
        IEnumerable<DataContent> images)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_accepting || index < Cursor) return;

            ChatMessage message = GroupTranscript.DeliveryMessage(text, images);
            if (await runner.TryInjectAsync(Member, message).ConfigureAwait(false)) _injected.Add((index, message));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 把一条附注（如后台任务跑完了）插进这一轮。被消费与否在封口或收尾时经 <paramref name="settle"/> 告知
    /// （true 为已消费），撤回来的由调用方另行补交。已封口或已收尾的不插
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    /// <param name="message">附注消息</param>
    /// <param name="settle">结局回调，在闸内同步调用</param>
    /// <returns>插进去了为 true</returns>
    public async Task<bool> InjectNoteAsync(IGroupMemberTurnRunner runner, ChatMessage message, Action<bool> settle)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_accepting || !await runner.TryInjectAsync(Member, message).ConfigureAwait(false)) return false;
            _notes.Add((message, settle));
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 封口：他说完了一段，不再接插话，并把队列里还没被消费的撤回来——否则框架见队列非空，
    /// 会让他为一句没点他名的插话再说一次（ADR 0049 修订）。撤回的由游标在下一轮照常投递；
    /// 点了他名的由调度器按「没被消费」补叫。封口后这一轮若因用户私聊插话续跑，群里的话也等下一轮
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    public async Task SealAsync(IGroupMemberTurnRunner runner)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _accepting = false;
            await WithdrawPendingAsync(runner).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 收尾：不再接插话，并把队列里还没被消费的撤回来——它们由游标在下一轮照常投递
    /// </summary>
    /// <param name="runner">成员一轮的跑法</param>
    /// <returns>插进去且确实被消费了的群流水下标</returns>
    public async Task<IReadOnlySet<int>> CloseAsync(IGroupMemberTurnRunner runner)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _accepting = false;
            await WithdrawPendingAsync(runner).ConfigureAwait(false);
            return _injected.Select(x => x.Index).ToHashSet();
        }
        finally
        {
            _gate.Release();
        }
    }

    // 撤回来的从登记里摘掉，留下的就是被消费了的；附注各自告知结局。须持有闸
    private async Task WithdrawPendingAsync(IGroupMemberTurnRunner runner)
    {
        if (_injected.Count == 0 && _notes.Count == 0) return;

        List<ChatMessage> pending = [.._injected.Select(x => x.Message), .._notes.Select(x => x.Message)];
        IReadOnlyCollection<ChatMessage> withdrawn;
        try
        {
            withdrawn = await runner.WithdrawAsync(Member, pending).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            // 撤不回来就按已消费算：最坏是晚到一次，不会重投
            Log.Warning($"Group member '{Member.Title}' failed to withdraw interjections: {e.Message}");
            withdrawn = [];
        }

        _injected.RemoveAll(x => withdrawn.Any(w => ReferenceEquals(w, x.Message)));
        foreach ((ChatMessage message, Action<bool> settle) in _notes)
        {
            settle(!withdrawn.Any(w => ReferenceEquals(w, message)));
        }

        _notes.Clear();
    }
}
