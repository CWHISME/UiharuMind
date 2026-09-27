using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Group;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 死波窗口：一波收场之后、从调度宿主摘掉之前，新发言得被明说「接不住」，宿主才会另开一波——
/// 否则那句只落盘、没人接，要等下次「继续」才补投。跑着的时候则必须接得住
/// </summary>
public class GroupSchedulerClosingTests
{
    private readonly FakeHost _host = new();
    private readonly ChatSession _group;

    public GroupSchedulerClosingTests()
    {
        _group = new ChatSession { Title = "会审", IsGroup = true, IsTransient = true };
        _group.GroupMemberSessionIds = ["alice", "bob"];
        _group.History.Add(new ChatMessage(ChatRole.User, "大家好"));
    }

    [Fact]
    public async Task Parallel_AfterIdle_RejectsNewPosts()
    {
        using GroupRun run = new(_group);
        ParallelGroupScheduler scheduler = new(_host, run);

        await scheduler.RunAsync(new GroupKickoff(0));

        Assert.True(scheduler.IsFinished);
        Assert.False(scheduler.OnPosted(new GroupPostEvent(1, null, "还有一句")));
    }

    [Fact]
    public async Task Parallel_WhileSomeoneSpeaks_AcceptsNewPosts()
    {
        TaskCompletionSource gate = new();
        _host.During = gate.Task;
        using GroupRun run = new(_group);
        ParallelGroupScheduler scheduler = new(_host, run);

        Task running = scheduler.RunAsync(new GroupKickoff(0));

        Assert.False(scheduler.IsFinished);
        Assert.True(scheduler.OnPosted(new GroupPostEvent(1, null, "还有一句")));
        gate.SetResult();
        await running;
    }

    [Fact]
    public async Task Serial_AfterTheRound_RejectsNewPosts()
    {
        using GroupRun run = new(_group);
        SerialGroupScheduler scheduler = new(_host, run);

        Assert.True(scheduler.OnPosted(new GroupPostEvent(1, null, "开跑前")));
        await scheduler.RunAsync(new GroupKickoff(0));

        Assert.True(scheduler.IsFinished);
        Assert.False(scheduler.OnPosted(new GroupPostEvent(1, null, "还有一句")));
    }

    private sealed class FakeHost : IGroupTurnHost
    {
        public Task During { get; set; } = Task.CompletedTask;

        public async Task<GroupTurnOutcome> RunMemberAsync(GroupRun run, string memberSessionId, EGroupWakeCause cause)
        {
            await During;
            return new GroupTurnOutcome(true, new HashSet<int>());
        }

        public bool HasNewLines(ChatSession group, string memberSessionId) => false;

        public IReadOnlyList<GroupRosterEntry> RosterOf(ChatSession group) =>
            group.GroupMemberSessionIds.Select(x => new GroupRosterEntry(x, x)).ToList();
    }
}
