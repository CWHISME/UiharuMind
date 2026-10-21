using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Group;

// 私聊叫停群轮之后的接回（ADR 0063）：成员由这里在闸空时单独叫醒。
// 私聊即正常消息：化身不再抢占/接回——它跑着时用户私聊走插话，闲着时私聊轮占闸、离席轮自己 Busy 退避
public sealed partial class GroupChatCoordinator
{
    private readonly HashSet<string> _preempted = new(); //被用户私聊叫停的成员：私聊结束后叫醒接着做
    private readonly HashSet<string> _resumeArmed = new(); //已约好闸空叫醒的成员：一人只约一次，免得多叫一轮
    /// <summary>
    /// 私聊放闸后等多久再叫醒成员：给用户留看完私聊回复的时间（固定 30 秒）。
    /// （化身不再有私聊接回，私聊即正常消息；这条只服务成员）
    /// </summary>
    private static readonly TimeSpan PrivateResumeDelay = TimeSpan.FromSeconds(30);

    // 用户停了群：成员不再自动接回（留给「继续」）
    private void ForgetResumes(string groupId)
    {
        if (_load(groupId) is not { IsGroup: true } group) return;
        lock (_locker) _preempted.ExceptWith(group.GroupMemberSessionIds);
    }

    // 还等着接回、又没约过的，约好闸一空就叫醒他
    private void ArmResume(ChatSession group, string memberSessionId)
    {
        lock (_locker)
        {
            if (!_preempted.Contains(memberSessionId) || !_resumeArmed.Add(memberSessionId)) return;
        }

        GroupMemberTurnGate.ResumeAfter(memberSessionId, () =>
        {
            int mark;
            lock (_locker)
            {
                _resumeArmed.Remove(memberSessionId);
                mark = _load(memberSessionId)?.History.Count ?? 0;
            }

            DelayThenResumeAsync(group, memberSessionId, mark).LogOnFault("resume a group member after private chat");
        });
    }

    // 私聊后接回的延迟唤醒：等的期间成员历史变长了（又私聊了一句）就重排完整间隔；
    // 不再待续（停群、退群）就不叫了，醒来那一下仍按待续判（`WakeMemberAsync` 的 pending）
    private async Task DelayThenResumeAsync(ChatSession group, string memberSessionId, int mark)
    {
        while (true)
        {
            await _delay(PrivateResumeDelay).ConfigureAwait(false);
            lock (_locker)
            {
                if (!_preempted.Contains(memberSessionId) || !group.GroupMemberSessionIds.Contains(memberSessionId))
                    return;
                int now = _load(memberSessionId)?.History.Count ?? 0;
                if (now <= mark) break; //没再私聊：去叫醒
                mark = now; //又私聊了一句：重排完整间隔
            }
        }

        await WakeMemberAsync(group, memberSessionId,
            () => _preempted.Contains(memberSessionId) && group.GroupMemberSessionIds.Contains(memberSessionId));
    }

    /// <summary>
    /// 单独叫醒一位成员：在跑的那一波接得住就交给它；接不住（已收场，或串行一圈定死了顺序）就等它摘掉，再单为他开一波
    /// </summary>
    /// <param name="group">群壳会话</param>
    /// <param name="memberSessionId">成员会话标识</param>
    /// <param name="pending">还要不要叫（持 _locker 时判）：等的这段时间里可能已随别的投递交出去、或不该再叫了</param>
    private async Task WakeMemberAsync(ChatSession group, string memberSessionId, Func<bool> pending)
    {
        while (true)
        {
            lock (_locker)
            {
                if (!pending()) return;
            }

            if (EpisodeOf(group.SessionId) is { } running)
            {
                if (running.Scheduler.OnMemberNoted(memberSessionId)) return;
                await running.Removed.Task;
                continue;
            }

            if (await RunEpisodeAsync(group, new GroupKickoff(null, memberSessionId))) return;
        }
    }
}
