using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>唤醒边界与 @ 解析（ADR 0049 决策 3、5、6）：纯函数</summary>
public class GroupWakePolicyTests
{
    private static readonly IReadOnlyList<string> Three = ["a", "b", "c"];
    private static readonly IReadOnlyList<string> Two = ["a", "b"];

    [Fact]
    public void UserPost_LargeGroup()
    {
        Assert.Equal(["a", "b", "c"], Targets(GroupWakePolicy.ForUserPost(Three, null, [])));
        Assert.Equal(["c"], Targets(GroupWakePolicy.ForUserPost(Three, "c", [])));
        Assert.Equal(["b"], Targets(GroupWakePolicy.ForUserPost(Three, "c", ["b"]))); //@ 了人就不叫主持人
        Assert.All(GroupWakePolicy.ForUserPost(Three, "c", []), x => Assert.Equal(EGroupWakeCause.User, x.Cause));
    }

    /// <summary>补位（ADR 0049 修订）：只激进档；有没看过的发言、这一波没跑成过的不补</summary>
    [Fact]
    public void CatchUp_AggressiveOnly_UnreadAndNotUnfinished()
    {
        HashSet<string> unread = ["a", "b"];
        HashSet<string> unfinished = ["b"];

        Assert.Empty(GroupWakePolicy.ForCatchUp(Three, EGroupStopPolicy.Conservative, unread.Contains, unfinished));
        IReadOnlyList<GroupWake> wakes =
            GroupWakePolicy.ForCatchUp(Three, EGroupStopPolicy.Aggressive, unread.Contains, unfinished);
        Assert.Equal(["a"], Targets(wakes));
        Assert.All(wakes, x => Assert.Equal(EGroupWakeCause.CatchUp, x.Cause));
    }

    [Fact]
    public void UserPost_SmallGroup_WakesEveryone()
    {
        Assert.Equal(["a", "b"], Targets(GroupWakePolicy.ForUserPost(Two, null, ["a"])));
    }

    [Fact]
    public void MemberPost_LargeGroup_OnlyMentionsWake()
    {
        Assert.Empty(GroupWakePolicy.ForMemberPost(Three, null, EGroupStopPolicy.Conservative, "a",
            EGroupWakeCause.User, []));
        IReadOnlyList<GroupWake> wakes = GroupWakePolicy.ForMemberPost(Three, null, EGroupStopPolicy.Conservative,
            "a", EGroupWakeCause.User, ["a", "b"]);
        Assert.Equal([new GroupWake("b", EGroupWakeCause.Member)], wakes); //@ 自己不算
    }

    [Fact]
    public void MemberPost_FromHost_WakesWithHostCause()
    {
        IReadOnlyList<GroupWake> wakes = GroupWakePolicy.ForMemberPost(Three, "c", EGroupStopPolicy.Conservative,
            "c", EGroupWakeCause.User, ["a"]);
        Assert.Equal([new GroupWake("a", EGroupWakeCause.Host)], wakes);
    }

    [Fact]
    public void MemberPost_SmallGroup_TheOtherMustAnswer()
    {
        Assert.Equal(["b"], Targets(GroupWakePolicy.ForMemberPost(Two, null, EGroupStopPolicy.Conservative, "a",
            EGroupWakeCause.User, [])));
    }

    [Theory]
    [InlineData(EGroupStopPolicy.Conservative, EGroupWakeCause.Member, 0)]
    [InlineData(EGroupStopPolicy.Conservative, EGroupWakeCause.Host, 1)]
    [InlineData(EGroupStopPolicy.Aggressive, EGroupWakeCause.Member, 1)]
    public void MemberPost_OneHopGuard(EGroupStopPolicy policy, EGroupWakeCause authorCause, int expected)
    {
        Assert.Equal(expected, GroupWakePolicy.ForMemberPost(Three, null, policy, "a", authorCause, ["b"]).Count);
    }

    [Theory]
    [InlineData("@白井黑子 你看", "kuroko")] //长名优先，不被「白井」吃掉
    [InlineData("@白井 你看", "shirai")]
    [InlineData("＠alice 你看", "alice")] //全角、大小写不敏感
    [InlineData("发邮件到 a@b.com", "")] //b 不是成员，本来就不会命中
    [InlineData("发邮件到 a@alice.com", "")] //@ 前是字母数字，邮箱写法不算点名（alice 是成员，要防误认）
    [InlineData("@Alice @alice", "alice")] //去重
    [InlineData("@Alice和@白井黑子", "alice,kuroko")]
    public void Mentions_AreParsedAgainstTheRoster(string text, string expected)
    {
        GroupRosterEntry[] roster =
        [
            new("alice", "Alice"),
            new("shirai", "白井"),
            new("kuroko", "白井黑子"),
        ];

        Assert.Equal(expected, string.Join(",", GroupMentions.Parse(text, roster)));
    }

    private static List<string> Targets(IReadOnlyList<GroupWake> wakes) => wakes.Select(x => x.MemberSessionId).ToList();
}
