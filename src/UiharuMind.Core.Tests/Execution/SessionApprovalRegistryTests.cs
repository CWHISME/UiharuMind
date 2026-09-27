using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 嵌套审批登记处：Core 登记等待、子窗口按引用认领、超时/取消按拒绝收口。
/// 不起模型——认领只看引用同一性，超时只看挂钟。
/// </summary>
public class SessionApprovalRegistryTests
{
    private static ToolApprovalRequestContent Request(string callId) =>
        new(callId, new FunctionCallContent(callId, "Write", null));

    private static Task<ChatMessage> Decision(string text) =>
        Task.FromResult(new ChatMessage(ChatRole.User, text));

    [Fact]
    public async Task Waiter_GetsAdoptedDecision()
    {
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        Task<IReadOnlyList<ChatMessage>> wait =
            registry.WaitForDecisionsAsync("sub-1", [request], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        ChatMessage decision = new(ChatRole.User, "go ahead");
        Assert.True(registry.TryAdopt("sub-1", request, Task.FromResult(decision)));

        IReadOnlyList<ChatMessage> responses = await wait;
        Assert.Same(decision, responses[0]);
    }

    [Fact]
    public async Task ManyAdopts_FirstDecisionWins()
    {
        // 切走再切回会重画一张卡,多窗口也各画一张:认领几次都行,先点出决定的那张算数
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        Task<IReadOnlyList<ChatMessage>> wait =
            registry.WaitForDecisionsAsync("sub-1", [request], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        TaskCompletionSource<ChatMessage> untouched = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Assert.True(registry.TryAdopt("sub-1", request, untouched.Task)); //没人点的那张卡
        ChatMessage clicked = new(ChatRole.User, "clicked");
        Assert.True(registry.TryAdopt("sub-1", request, Task.FromResult(clicked)));

        IReadOnlyList<ChatMessage> responses = await wait;
        Assert.Same(clicked, responses[0]);

        untouched.SetResult(new ChatMessage(ChatRole.User, "too late")); //迟到的决定落空
        Assert.Same(clicked, (await wait)[0]);
    }

    [Fact]
    public void Adopt_MissesOtherSessionAndOtherRequest()
    {
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        Task<IReadOnlyList<ChatMessage>> wait =
            registry.WaitForDecisionsAsync("sub-1", [request], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.False(registry.TryAdopt("sub-2", request, Decision("x")));
        Assert.False(registry.TryAdopt("sub-1", Request("b"), Decision("x")));
        Assert.False(wait.IsCompleted);
    }

    [Fact]
    public async Task PendingAdded_LetsLateSideAdopt()
    {
        // 卡片先于登记诞生时,认领落在这一侧:登记喊一声,界面回头认一遍
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        ChatMessage decision = new(ChatRole.User, "go ahead");
        registry.PendingAdded += sessionId =>
            registry.TryAdopt(sessionId, request, Task.FromResult(decision));

        IReadOnlyList<ChatMessage> responses = await registry.WaitForDecisionsAsync(
            "sub-1", [request], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(decision, responses[0]);
    }

    /// <summary>
    /// 群的待审批条靠快照 + 变化通知：登记进来就列出，同批里先点的那一条当场撤掉（不等其余几条），收尾全撤
    /// </summary>
    [Fact]
    public async Task PendingOf_TracksWhatIsStillUndecided()
    {
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent a = Request("a");
        ToolApprovalRequestContent b = Request("b");
        List<string> changes = [];
        registry.PendingChanged += sessionId =>
        {
            lock (changes) changes.Add(sessionId);
        };

        Task<IReadOnlyList<ChatMessage>> wait = registry.WaitForDecisionsAsync("member-1", [a, b],
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal([a, b], registry.PendingOf("member-1"));
        Assert.Empty(registry.PendingOf("member-2"));

        registry.TryAdopt("member-1", a, Decision("yes"));
        Assert.Equal([b], registry.PendingOf("member-1"));

        registry.TryAdopt("member-1", b, Decision("no"));
        await wait;
        Assert.Empty(registry.PendingOf("member-1"));
        lock (changes) Assert.Equal(["member-1", "member-1", "member-1", "member-1"], changes); //登记、a、b、收尾
    }

    /// <summary>
    /// 同一次审批画在好几处：别处先点了，其余几张据此收起；窗口晚开、补画出来的卡在决出之后也查得到结果
    /// </summary>
    [Fact]
    public async Task DecisionOf_FollowsTheFirstAnswer_AndOutlivesTheWait()
    {
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        Task<IReadOnlyList<ChatMessage>> wait = registry.WaitForDecisionsAsync("member-1", [request],
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Task<ChatMessage> seenByAnotherCard = registry.DecisionOf("member-1", request)!;
        Assert.False(seenByAnotherCard.IsCompleted);

        ChatMessage clicked = new(ChatRole.User, "approved in the group strip");
        registry.TryAdopt("member-1", request, Task.FromResult(clicked));
        Assert.Same(clicked, await seenByAnotherCard);
        await wait;

        Assert.Same(clicked, await registry.DecisionOf("member-1", request)!); //登记撤了，结果还在
        Assert.Null(registry.DecisionOf("member-2", request));
        Assert.Null(registry.DecisionOf("member-1", Request("b")));
    }

    [Fact]
    public async Task Timeout_DeniesUnfinished()
    {
        SessionApprovalRegistry registry = new();
        IReadOnlyList<ChatMessage> responses = await registry.WaitForDecisionsAsync(
            "sub-1", [Request("a")], TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        // 具象回应类型是框架内部的，只断结构：一一对应、用户角色、单条内容
        ChatMessage only = Assert.Single(responses);
        Assert.Equal(ChatRole.User, only.Role);
        Assert.Single(only.Contents);
    }

    [Fact]
    public async Task Cancel_DeniesUnfinished()
    {
        SessionApprovalRegistry registry = new();
        using CancellationTokenSource source = new();
        Task<IReadOnlyList<ChatMessage>> wait =
            registry.WaitForDecisionsAsync("sub-1", [Request("a")], TimeSpan.FromMinutes(5), source.Token);

        source.Cancel();
        IReadOnlyList<ChatMessage> responses = await wait;
        ChatMessage only = Assert.Single(responses);
        Assert.Equal(ChatRole.User, only.Role);
        Assert.Single(only.Contents);
    }

    [Fact]
    public async Task Adopt_AfterWaitEnds_Misses()
    {
        SessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        await registry.WaitForDecisionsAsync("sub-1", [request], TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.False(registry.TryAdopt("sub-1", request, Decision("x")));
    }
}
