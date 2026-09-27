namespace UiharuMind.Core.AI.Chat.Group;

/// <summary>要叫醒的一位成员，以及因何叫醒</summary>
/// <param name="MemberSessionId">成员会话标识</param>
/// <param name="Cause">来由</param>
public readonly record struct GroupWake(string MemberSessionId, EGroupWakeCause Cause);

/// <summary>
/// 并行群聊的唤醒边界（ADR 0049 决策 3、4、6）：一条发言之后该叫醒谁。全是纯函数。
/// 正在跑的成员不靠这里——他们经实时广播收到每一条（决策 2）
/// </summary>
public static class GroupWakePolicy
{
    private const int SmallGroupMaxMembers = 2; //≤2 人算小群：用户是天然主持人，成员之间必答

    /// <summary>
    /// 用户发言叫醒谁：小群全员；@ 了人就只叫被 @ 的；没 @ 有主持人叫主持人（由他点名）；都没有全员
    /// </summary>
    /// <param name="members">成员会话标识，发言顺序</param>
    /// <param name="hostSessionId">主持人；没有为 null</param>
    /// <param name="mentions">这句 @ 到的成员</param>
    /// <returns>要叫醒的成员</returns>
    public static IReadOnlyList<GroupWake> ForUserPost(IReadOnlyList<string> members, string? hostSessionId,
        IReadOnlyList<string> mentions)
    {
        IEnumerable<string> targets;
        if (members.Count <= SmallGroupMaxMembers) targets = members;
        else if (mentions.Count > 0) targets = mentions;
        else if (hostSessionId != null && members.Contains(hostSessionId)) targets = [hostSessionId];
        else targets = members;

        return targets.Where(members.Contains).Select(x => new GroupWake(x, EGroupWakeCause.User)).ToList();
    }

    /// <summary>
    /// 成员发言叫醒谁：@ 了谁叫谁；没 @ 时小群叫另一位（必答一次），大群不叫任何人。
    /// 保守档下，被别的成员叫醒的人说完不再叫醒任何人（一跳防护）；主持人点名算主持人叫醒，不受这条限制
    /// </summary>
    /// <param name="members">成员会话标识，发言顺序</param>
    /// <param name="hostSessionId">主持人；没有为 null</param>
    /// <param name="stopPolicy">停止条件</param>
    /// <param name="authorSessionId">发言人</param>
    /// <param name="authorCause">发言人这一轮因何被叫醒</param>
    /// <param name="mentions">这句 @ 到的成员</param>
    /// <returns>要叫醒的成员</returns>
    public static IReadOnlyList<GroupWake> ForMemberPost(IReadOnlyList<string> members, string? hostSessionId,
        EGroupStopPolicy stopPolicy, string authorSessionId, EGroupWakeCause authorCause,
        IReadOnlyList<string> mentions)
    {
        if (stopPolicy == EGroupStopPolicy.Conservative && authorCause == EGroupWakeCause.Member) return [];

        EGroupWakeCause cause = authorSessionId == hostSessionId ? EGroupWakeCause.Host : EGroupWakeCause.Member;
        List<string> targets = mentions.Where(x => x != authorSessionId && members.Contains(x)).ToList();
        if (targets.Count == 0 && members.Count <= SmallGroupMaxMembers)
        {
            targets = members.Where(x => x != authorSessionId).ToList();
        }

        return targets.Select(x => new GroupWake(x, cause)).ToList();
    }
}
