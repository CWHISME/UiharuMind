using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 子代理的审批通道：有人看着就登记到子会话等点选，连续被拒才封顶。
/// 不起模型——登记处在本地构造。
///
/// 子代理默认后台化之后<b>不再先问派活者那一轮</b>（那一轮在子代理开跑前就结束了，
/// 恒定接不住），见 ADR 0025。
/// </summary>
public class NestedApprovalResolverTests
{
    private const string SessionId = "sub-1";

    private static ToolApprovalRequestContent Request(string callId) =>
        new(callId, new FunctionCallContent(callId, "Write", null));

    private static ApprovalResolver Create(SubSessionApprovalRegistry registry,
        int maxDeniedRounds = 2, TimeSpan? timeout = null, Action? onWaiting = null)
    {
        ApprovalResolver? resolver = NestedApprovalResolver.Create(attended: true, SessionId, registry,
            timeout ?? TimeSpan.FromMilliseconds(50), maxDeniedRounds, CancellationToken.None, onWaiting);
        Assert.NotNull(resolver);
        return resolver;
    }

    [Fact]
    public void Unattended_MeansNoApprovalRound()
    {
        //没人看着就别去登记处白等一轮超时:上游当场拒绝
        Assert.Null(NestedApprovalResolver.Create(attended: false, SessionId, new SubSessionApprovalRegistry(),
            TimeSpan.FromSeconds(1), 2, CancellationToken.None));
    }

    [Fact]
    public async Task Attended_AlwaysRegistersForUserDecision()
    {
        SubSessionApprovalRegistry registry = new();
        ChatMessage decision = new(ChatRole.User, "user clicked");
        ToolApprovalRequestContent request = Request("a");
        registry.PendingAdded += sessionId =>
            registry.TryAdopt(sessionId, request, Task.FromResult(decision));

        ApprovalResolver resolver = Create(registry);
        IReadOnlyList<ChatMessage> responses = await resolver([request]);

        Assert.Same(decision, Assert.Single(responses));
    }

    /// <summary>
    /// 「有审批在等你」那条提示是<b>承重</b>的：后台跑着没人盯那张卡，超时就按拒绝收口、
    /// 那次委派白跑。不钉住的话它很容易在某次重构里被顺手删掉（看起来只是个提示）
    /// </summary>
    [Fact]
    public async Task WaitingForUser_FiresTheNotice()
    {
        int notices = 0;
        ApprovalResolver resolver = Create(new SubSessionApprovalRegistry(), onWaiting: () => notices++);

        await resolver([Request("a")]);

        Assert.Equal(1, notices);
    }

    [Fact]
    public async Task ConsecutiveDenials_EndTheRun()
    {
        // 无人点选 → 超时按拒绝收口,连续两轮到顶,第三轮返空让轮次正常结束
        SubSessionApprovalRegistry registry = new();
        ApprovalResolver resolver = Create(registry);

        Assert.Single(await resolver([Request("a")]));
        Assert.Single(await resolver([Request("b")]));
        Assert.Empty(await resolver([Request("c")]));
    }

    [Fact]
    public async Task Approval_ResetsTheDeniedCount()
    {
        // 用户老老实实点允许的长任务不该被计数器掐掉:批准一次即清零
        SubSessionApprovalRegistry registry = new();
        ApprovalResolver resolver = Create(registry);

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

    /// <summary>
    /// 完全自动档:卡弹在子会话窗口,盯着主会话的用户看不见——等超时再拒等于整轮白跑。
    /// 自动放行不登记、不等人、不弹提示,但决定理由照给(送模型)、点名照调(进报告)。
    /// </summary>
    [Fact]
    public async Task FullAuto_ApprovesWithoutWaitingForAClick()
    {
        SubSessionApprovalRegistry registry = new();
        bool registered = false;
        registry.PendingAdded += _ => registered = true;
        int notices = 0;
        int autoNamed = 0;
        ApprovalResolver? resolver = NestedApprovalResolver.Create(attended: true, SessionId, registry,
            TimeSpan.FromSeconds(5), 2, CancellationToken.None, onWaiting: () => notices++,
            autoApprove: _ => "FullAuto", onAutoApproved: _ => autoNamed++);
        Assert.NotNull(resolver);

        IReadOnlyList<ChatMessage> responses = await resolver([Request("a")]);

        ToolApprovalResponseContent approval = Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(responses).Contents));
        Assert.True(approval.Approved);
        Assert.False(registered);
        Assert.Equal(0, notices);
        Assert.Equal(1, autoNamed);
    }

    /// <summary>
    /// 嵌套审批自动放行的守门：只有「有人守着 × 完全自动档」才放行——无人值守不放
    /// （没人看着时静默改盘比停下更糟），低档位照旧问人（ADR 0010 的例外登记见 ADR 0032）。
    /// 四个象限一次定死，防止以后哪条分支改语义时漏掉。
    /// </summary>
    [Theory]
    [InlineData(true, (int)EAgentPermissionMode.FullAuto, true)]
    [InlineData(false, (int)EAgentPermissionMode.FullAuto, false)]
    [InlineData(true, (int)EAgentPermissionMode.AutoEdit, false)]
    [InlineData(false, (int)EAgentPermissionMode.AutoEdit, false)]
    [InlineData(true, (int)EAgentPermissionMode.ReadOnly, false)]
    public void ShouldAutoApproveNestedApprovals_OnlyWhenAttendedFullAuto(
        bool attended, int modeIndex, bool expected)
    {
        Assert.Equal(expected, SubAgentTool.ShouldAutoApproveNestedApprovals(attended, modeIndex));
    }
}
