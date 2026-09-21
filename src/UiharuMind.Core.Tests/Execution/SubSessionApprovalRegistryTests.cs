using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 嵌套审批登记处：Core 登记等待、子窗口按引用认领、超时/取消按拒绝收口。
/// 不起模型——认领只看引用同一性，超时只看挂钟。
/// </summary>
public class SubSessionApprovalRegistryTests
{
    private static ToolApprovalRequestContent Request(string callId) =>
        new(callId, new FunctionCallContent(callId, "Write", null));

    private static Task<ChatMessage> Decision(string text) =>
        Task.FromResult(new ChatMessage(ChatRole.User, text));

    [Fact]
    public async Task Waiter_GetsAdoptedDecision()
    {
        SubSessionApprovalRegistry registry = new();
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
        SubSessionApprovalRegistry registry = new();
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
        SubSessionApprovalRegistry registry = new();
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
        SubSessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        ChatMessage decision = new(ChatRole.User, "go ahead");
        registry.PendingAdded += sessionId =>
            registry.TryAdopt(sessionId, request, Task.FromResult(decision));

        IReadOnlyList<ChatMessage> responses = await registry.WaitForDecisionsAsync(
            "sub-1", [request], TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

        Assert.Same(decision, responses[0]);
    }

    [Fact]
    public async Task Timeout_DeniesUnfinished()
    {
        SubSessionApprovalRegistry registry = new();
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
        SubSessionApprovalRegistry registry = new();
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
        SubSessionApprovalRegistry registry = new();
        ToolApprovalRequestContent request = Request("a");
        await registry.WaitForDecisionsAsync("sub-1", [request], TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);

        Assert.False(registry.TryAdopt("sub-1", request, Decision("x")));
    }
}
