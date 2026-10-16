using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 无人值守的审批通道：一条都不问、逐条按判据收口，带轮次上限（定时任务、群离席、目标模式共用）
/// </summary>
public class UnattendedApprovalsTests
{
    [Fact]
    public async Task Verdict_GoesOutAsTheResponse_WithItsReason()
    {
        ApprovalResolver resolver = Create(_ => new UnattendedVerdict(true, "在推进"));

        IReadOnlyList<ChatMessage> responses =
            await resolver([Request("r1", "Write"), Request("r2", "Shell")]);

        Assert.Equal(2, responses.Count);
        foreach (ChatMessage response in responses)
        {
            ToolApprovalResponseContent content =
                Assert.Single(response.Contents.OfType<ToolApprovalResponseContent>());
            Assert.True(content.Approved);
            Assert.Equal("在推进", content.Reason);
        }
    }

    /// <summary>每批一条都不批就算一轮空转：攒够轮次就收工，别让模型换着花样一直要</summary>
    [Fact]
    public async Task NothingApprovedRoundsInARow_EndsTheRun()
    {
        ApprovalResolver resolver = Create(_ => new UnattendedVerdict(false, "不批"), maxDeniedRounds: 2);

        Assert.NotEmpty(await resolver([Request("r1", "Write")]));
        Assert.NotEmpty(await resolver([Request("r2", "Write")]));
        Assert.Empty(await resolver([Request("r3", "Write")]));
    }

    /// <summary>批过一回就清零：老老实实配合的活不该被自己的计数器掐掉</summary>
    [Fact]
    public async Task OneApproval_ResetsTheDeniedStreak()
    {
        ApprovalResolver resolver = Create(request => request.ToolCall is FunctionCallContent { Name: "Read" }
            ? new UnattendedVerdict(true, "只读放行")
            : new UnattendedVerdict(false, "不批"), maxDeniedRounds: 2);

        Assert.NotEmpty(await Answer(resolver, "Write"));
        Assert.NotEmpty(await Answer(resolver, "Read")); //批过：空转计数归零
        Assert.NotEmpty(await Answer(resolver, "Write"));
        Assert.NotEmpty(await Answer(resolver, "Write")); //这才攒够两轮空转
        Assert.Empty(await Answer(resolver, "Write"));
    }

    /// <summary>总轮次上限：一律批的策略永远不会触发「一条都没批」，靠这一条收</summary>
    [Fact]
    public async Task TotalRoundsCap_StopsAnAlwaysApprovePolicy()
    {
        ApprovalResolver resolver = Create(_ => new UnattendedVerdict(true, "批"), maxRounds: 2);

        Assert.NotEmpty(await resolver([Request("r1", "Write")]));
        Assert.NotEmpty(await resolver([Request("r2", "Write")]));
        Assert.Empty(await resolver([Request("r3", "Write")]));
    }

    [Fact]
    public async Task EveryDecision_IsHandedToTheLedger()
    {
        List<string> decided = [];
        ApprovalResolver resolver = Create(_ => new UnattendedVerdict(false, "不批"),
            onDecided: (request, verdict) => decided.Add($"{Describe(request)}={verdict.Approved}"));

        await resolver([Request("r1", "Write")]);

        Assert.Equal(["Write=False"], decided);
    }

    [Fact]
    public async Task NoRequests_NothingToAnswer()
    {
        Assert.Empty(await Create(_ => new UnattendedVerdict(true, "批"))([]));
    }

    /// <summary>日志点名要说得清这次调用动哪儿：shell 带命令、写文件带路径</summary>
    [Fact]
    public void Describe_NamesTheCallAndWhatItTouches()
    {
        Assert.Equal("Write[src/a.txt]", Describe(Request("r1", "Write", ("filePath", "src/a.txt"))));
        Assert.Equal("Shell[ls -la]", Describe(Request("r2", "Shell", ("command", "ls -la"))));
        Assert.Equal("Read", Describe(Request("r3", "Read")));
    }

    /// <summary>开关每批现问、通道按执行冻结：多批共用首个通道，限位不断</summary>
    [Fact]
    public async Task Scope_ReusesTheFirstChannelAcrossBatches()
    {
        UnattendedScope scope = new(() => Create(_ => new UnattendedVerdict(true, "批"), maxRounds: 2));

        Assert.NotEmpty(await scope.ForBatch()!([Request("r1", "Write")]));
        Assert.NotEmpty(await scope.ForBatch()!([Request("r2", "Write")]));
        Assert.Empty(await scope.ForBatch()!([Request("r3", "Write")])); //同一通道的计数到了顶
    }

    /// <summary>撤销立即生效：开关变 null，下一批就回落，不再用冻结的通道</summary>
    [Fact]
    public async Task Scope_ReleasesTheChannel_WhenTheSwitchTurnsOff()
    {
        bool on = true;
        UnattendedScope scope = new(() => on ? Create(_ => new UnattendedVerdict(true, "批")) : null);

        Assert.NotNull(scope.ForBatch());
        on = false;
        Assert.Null(scope.ForBatch());
    }

    /// <summary>中途授予：之前没冻结，取到就冻结，后续批次复用它</summary>
    [Fact]
    public async Task Scope_FreezesTheChannel_WhenTheSwitchTurnsOnMidExecution()
    {
        bool on = false;
        int created = 0;
        UnattendedScope scope = new(() =>
        {
            if (!on) return null;
            created++;
            return Create(_ => new UnattendedVerdict(true, "批"), maxRounds: 1);
        });

        Assert.Null(scope.ForBatch());
        on = true;
        Assert.NotNull(scope.ForBatch());
        Assert.NotEmpty(await scope.ForBatch()!([Request("r1", "Write")]));
        Assert.Empty(await scope.ForBatch()!([Request("r2", "Write")])); //冻结的是同一个通道：上限生效
        // 开关是每批重问的（撤销靠它发现）：创建次数等于授予期间的 ForBatch 次数，断的是重问语义不是浪费
        Assert.Equal(3, created);
    }

    /// <summary>新执行重取：上一执行的计数不能带过来</summary>
    [Fact]
    public void Scope_Reset_StartsTheNextExecutionFresh()
    {
        UnattendedScope scope = new(() => Create(_ => new UnattendedVerdict(true, "批"), maxRounds: 1));
        ApprovalResolver? first = scope.ForBatch();

        scope.Reset();

        Assert.NotSame(first, scope.ForBatch());
    }

    private static Task<IReadOnlyList<ChatMessage>> Answer(ApprovalResolver resolver, string tool) =>
        resolver([Request("r", tool)]);

    private static ApprovalResolver Create(Func<ToolApprovalRequestContent, UnattendedVerdict> decide,
        int? maxRounds = null, int? maxDeniedRounds = 3,
        Action<ToolApprovalRequestContent, UnattendedVerdict>? onDecided = null) =>
        UnattendedApprovals.Create("tests", request => Task.FromResult(decide(request)), maxRounds, maxDeniedRounds,
            onDecided);

    private static string Describe(ToolApprovalRequestContent request) => UnattendedApprovals.Describe(request);

    private static ToolApprovalRequestContent Request(string id, string tool,
        params (string Key, object? Value)[] args) =>
        new(id, new FunctionCallContent(id, tool, args.ToDictionary(x => x.Key, x => x.Value)));
}
