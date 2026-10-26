using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 群的「等这一波跑完」订阅。钉的是**时序**：订阅必须早于发帖——
/// <see cref="GroupChatCoordinator.PostAsync"/> 的同步段就可能把整波跑完（群里没可叫醒的人时调度器直接返回），
/// 事后再订阅就永远等不到那一次 EpisodeEnded
/// </summary>
public sealed class GroupEpisodeWatchTests
{
    private readonly GroupChatCoordinator _coordinator =
        new(new FakeGroupMemberTurnRunner(), id => null);

    [Fact]
    public async Task SubscribeBeforePost_CatchesAnEpisodeThatEndsRightAway()
    {
        // 一个没有成员的群：这一波开完当场收场，收场就发生在 PostAsync 调用里
        ChatSession group = new() { Title = "空群", IsGroup = true, IsAgentGroup = true, IsTransient = true };

        using GroupEpisodeWatch watch = GroupEpisodeWatch.Begin(_coordinator, group.SessionId);
        await _coordinator.PostAsync(group, "有人吗");

        GroupEpisodeSummary summary = await watch.NextAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(group, summary.Group);
        Assert.True(summary.End > summary.Start); //这一波收场时那句用户发言已经落盘
    }

    [Fact]
    public async Task OnlyTheWatchedGroupIsReported()
    {
        ChatSession mine = new() { Title = "我的群", IsGroup = true, IsAgentGroup = true, IsTransient = true };
        ChatSession other = new() { Title = "别人的群", IsGroup = true, IsAgentGroup = true, IsTransient = true };

        using GroupEpisodeWatch watch = GroupEpisodeWatch.Begin(_coordinator, mine.SessionId);
        await _coordinator.PostAsync(other, "先跑别的群");
        await _coordinator.PostAsync(mine, "再跑我的");

        GroupEpisodeSummary summary = await watch.NextAsync(TestContext.Current.CancellationToken)
            .WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(mine, summary.Group);
    }
}
