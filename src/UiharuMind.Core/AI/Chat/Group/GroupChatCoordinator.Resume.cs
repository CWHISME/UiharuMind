using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>化身被私聊叫停之后的接回状态（ADR 0063）</summary>
public enum EGroupAvatarResume
{
    /// <summary>没有待接回的（没被叫停，或已经接回过）</summary>
    None,

    /// <summary>等私聊结束接回</summary>
    Pending,

    /// <summary>等的期间用户停了群：按停下处理，不直接接回</summary>
    Stopped,
}

// 私聊叫停群轮之后的接回（ADR 0063）：成员由这里在闸空时单独叫醒；化身只记状态，由离席接回
public sealed partial class GroupChatCoordinator
{
    private readonly HashSet<string> _preempted = new(); //被用户私聊叫停的成员：私聊结束后叫醒接着做
    private readonly HashSet<string> _resumeArmed = new(); //已约好闸空叫醒的成员：一人只约一次，免得多叫一轮
    private readonly Dictionary<string, EGroupAvatarResume> _avatarResumes = new(); //群 → 化身的接回状态

    /// <summary>
    /// 这个群的化身有没有被私聊叫停、等着接回
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    /// <returns>接回状态</returns>
    public EGroupAvatarResume AvatarResumeOf(string groupId)
    {
        lock (_locker) return _avatarResumes.GetValueOrDefault(groupId);
    }

    /// <summary>
    /// 化身那边已按「停下」处理了待接回：清掉记号
    /// </summary>
    /// <param name="groupId">群壳会话标识</param>
    public void ClearAvatarResume(string groupId)
    {
        lock (_locker) _avatarResumes.Remove(groupId);
    }

    // 化身这一轮的接回记号：抢到闸即算接回过（清掉），被叫停则记上
    private void NoteAvatarTurn(string groupId, bool preempted)
    {
        lock (_locker)
        {
            if (preempted) _avatarResumes[groupId] = EGroupAvatarResume.Pending;
            else _avatarResumes.Remove(groupId);
        }
    }

    // 用户停了群：成员不再自动接回（留给「继续」），化身改按停下处理
    private void ForgetResumes(string groupId)
    {
        if (_load(groupId) is not { IsGroup: true } group) return;
        lock (_locker)
        {
            _preempted.ExceptWith(group.GroupMemberSessionIds);
            if (_avatarResumes.ContainsKey(groupId)) _avatarResumes[groupId] = EGroupAvatarResume.Stopped;
        }
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
            lock (_locker) _resumeArmed.Remove(memberSessionId);
            // 等的期间被停群或退了群，就不叫了
            WakeMemberAsync(group, memberSessionId,
                    () => _preempted.Contains(memberSessionId) && group.GroupMemberSessionIds.Contains(memberSessionId))
                .LogOnFault("resume a group member after private chat");
        });
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
