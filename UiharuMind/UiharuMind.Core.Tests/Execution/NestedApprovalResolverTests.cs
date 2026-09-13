using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子代理的审批通道：父口优先、接不住转登记处、连续被拒才封顶。
/// 不起模型——父口与登记处都在本地构造。
/// </summary>
public class NestedApprovalResolverTests
{
    private const string SessionId = "sub-1";

    private static ToolApprovalRequestContent Request(string callId) =>
        new(callId, new FunctionCallContent(callId, "Write", null));

    /// <summary>父口恒空：子代理那一路的真实情形（父转录器本轮清单里没有这些请求）</summary>
    private static ApprovalResolver EmptyParent() => _ => Task.FromResult<IReadOnlyList<ChatMessage>>([]);

    private static ApprovalResolver Create(ApprovalResolver parent, SubSessionApprovalRegistry registry,
        int maxDeniedRounds = 2, TimeSpan? timeout = null)
    {
        ApprovalResolver? resolver = NestedApprovalResolver.Create(parent, SessionId, registry,
            timeout ?? TimeSpan.FromMilliseconds(50), maxDeniedRounds, CancellationToken.None);
        Assert.NotNull(resolver);
        return resolver;
    }

    [Fact]
    public void NoParentResolver_MeansNoApprovalRound()
    {
        Assert.Null(NestedApprovalResolver.Create(null, SessionId, new SubSessionApprovalRegistry(),
            TimeSpan.FromSeconds(1), 2, CancellationToken.None));
    }

    [Fact]
    public async Task ParentAnswer_WinsWithoutRegistering()
    {
        SubSessionApprovalRegistry registry = new();
        bool registered = false;
        registry.PendingAdded += _ => registered = true;

        ChatMessage answer = new(ChatRole.User, "parent says so");
        ApprovalResolver resolver = Create(_ => Task.FromResult<IReadOnlyList<ChatMessage>>([answer]), registry);

        IReadOnlyList<ChatMessage> responses = await resolver([Request("a")]);

        Assert.Same(answer, Assert.Single(responses));
        Assert.False(registered);
    }

    [Fact]
    public async Task EmptyParentAnswer_FallsThroughToRegistry()
    {
        SubSessionApprovalRegistry registry = new();
        ChatMessage decision = new(ChatRole.User, "user clicked");
        ToolApprovalRequestContent request = Request("a");
        registry.PendingAdded += sessionId =>
            registry.TryAdopt(sessionId, request, Task.FromResult(decision));

        ApprovalResolver resolver = Create(EmptyParent(), registry);
        IReadOnlyList<ChatMessage> responses = await resolver([request]);

        Assert.Same(decision, Assert.Single(responses));
    }

    [Fact]
    public async Task ConsecutiveDenials_EndTheRun()
    {
        // 无人点选 → 超时按拒绝收口,连续两轮到顶,第三轮返空让轮次正常结束
        SubSessionApprovalRegistry registry = new();
        ApprovalResolver resolver = Create(EmptyParent(), registry);

        Assert.Single(await resolver([Request("a")]));
        Assert.Single(await resolver([Request("b")]));
        Assert.Empty(await resolver([Request("c")]));
    }

    [Fact]
    public async Task Approval_ResetsTheDeniedCount()
    {
        // 用户老老实实点允许的长任务不该被计数器掐掉:批准一次即清零
        SubSessionApprovalRegistry registry = new();
        ApprovalResolver resolver = Create(EmptyParent(), registry);

        Assert.Single(await resolver([Request("a")])); //超时拒绝,计 1

        ToolApprovalRequestContent approved = Request("b");
        registry.PendingAdded += sessionId => registry.TryAdopt(sessionId, approved,
            Task.FromResult(new ChatMessage(ChatRole.User,
                [ToolApprovalResponseFactory.Create(approved, EApprovalDecision.Once, "ok")])));
        Assert.Single(await resolver([approved])); //批准,清零

        Assert.Single(await resolver([Request("c")]));
        Assert.Single(await resolver([Request("d")]));
        Assert.Empty(await resolver([Request("e")])); //再连续两轮被拒才到顶
    }
}
