using Microsoft.Extensions.AI;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>
/// 成员附注：群外发生、该由某位成员接着处理的事（他启动的后台任务跑完了）。
///
/// 不写进他自己的会话再起一轮：那一轮在群外跑，说完的话进不了群（ADR 0046 拿掉成员委派正是为此）。
/// 他正在说就插进那一轮，与群里的插话同一条路，醒着就及时收到；插不进或没被消费的挂在投递上——
/// 下次轮到他时排在群里新话之前交给他，并单独叫他开口一次，说完照常进群
/// </summary>
public sealed partial class GroupChatCoordinator
{
    private readonly Dictionary<string, List<string>> _notes = new(); //成员 → 待交的附注，与投递同一把锁

    /// <summary>
    /// 给成员送一条附注：他正在说就插进那一轮，否则挂起来并叫他开口一次。附注只在内存里：应用退出前没交出去的就没了
    /// </summary>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="note">附注消息（user 角色，插进那一轮时原样进他的历史）</param>
    /// <returns>交给了群为 true；他已不在群里为 false（由调用方另想办法）</returns>
    public async Task<bool> NotifyMemberAsync(string memberSessionId, ChatMessage note)
    {
        if (_load(memberSessionId) is not { IsGroupMember: true } member) return false;
        if (_load(member.GroupId!) is not { IsGroup: true } group) return false;
        if (!group.GroupMemberSessionIds.Contains(member.SessionId)) return false; //已退群

        // 查他在不在说与挂附注同在投递那把锁里：投递取附注与登记在说是一体的，附注要么随投递交、要么插进那一轮
        GroupMemberTurnState? turn;
        lock (_locker)
        {
            turn = EpisodeOf(group.SessionId)?.Run.TurnOf(member.SessionId);
            if (turn == null) AddNote(member.SessionId, note.Text);
        }

        if (turn != null && await InjectNoteAsync(turn, note)) return true;

        while (true)
        {
            if (EpisodeOf(group.SessionId) is { } running)
            {
                if (running.Scheduler.OnMemberNoted(member.SessionId)) return true;
                // 接不住(已收场或串行一圈定死了顺序):等它摘掉,再单独为他开一波
                await running.Removed.Task;
                continue;
            }

            // 等的这段时间里附注可能已随别的投递交出去(串行这一圈后面轮到了他):那就不必再单叫一波
            lock (_locker)
            {
                if (!_notes.ContainsKey(member.SessionId)) return true;
            }

            if (await RunEpisodeAsync(group, new GroupKickoff(null, member.SessionId))) return true;
        }
    }

    // 插进他正在跑的那一轮，等到封口或收尾才知道消费了没有。没插进去或被撤回的转回挂起，返回 false
    private async Task<bool> InjectNoteAsync(GroupMemberTurnState turn, ChatMessage note)
    {
        TaskCompletionSource<bool> settled = new(TaskCreationOptions.RunContinuationsAsynchronously);
        // 撤回的在闸内同步挂回：收尾一放闸，他的下一轮就可能来取附注
        void Settle(bool consumed)
        {
            if (!consumed)
            {
                lock (_locker) AddNote(turn.Member.SessionId, note.Text);
            }

            settled.TrySetResult(consumed);
        }

        if (await turn.InjectNoteAsync(_runner, note, Settle)) return await settled.Task;

        lock (_locker) AddNote(turn.Member.SessionId, note.Text);
        return false;
    }

    // 调用方持 _locker
    private void AddNote(string memberSessionId, string note)
    {
        if (!_notes.TryGetValue(memberSessionId, out List<string>? notes))
        {
            notes = [];
            _notes[memberSessionId] = notes;
        }

        notes.Add(note);
    }

    // 取走待交的附注接到投递前面。调用方持 _locker
    private string? TakeNotes(string memberSessionId, string? delivery)
    {
        if (!_notes.Remove(memberSessionId, out List<string>? notes)) return delivery;
        string joined = string.Join("\n\n", notes);
        return delivery == null ? joined : joined + "\n\n" + delivery;
    }
}
