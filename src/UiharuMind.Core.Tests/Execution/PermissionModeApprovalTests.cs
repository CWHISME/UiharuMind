using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 钉死三档权限的真实语义。这套曾经跑偏过一次，而且是无声跑偏：
/// <c>AutoEdit</c> 档一条规则都不加，行为与只读档完全一致，枚举注释却写着"文件读写自动放行"；
/// 定时任务代码写死用这一档、无头执行又一律拒绝审批，净效果是定时任务所有文件写入都被拒。
/// 档位的每一格都必须有测试盯着，否则"档位"就只是设置页上的三个字。
/// </summary>
public class PermissionModeApprovalTests
{
    private const string Root = "/tmp/uiharu-ws";

    private static FunctionCallContent Edit(string filePath) => new("c1", "Edit",
        new Dictionary<string, object?> { ["filePath"] = filePath });

    private static FunctionCallContent Shell(string command) => new("c2", CharacterRunnerFactory.ShellToolName,
        new Dictionary<string, object?> { ["command"] = command });

    private static Task<bool> ApprovedAsync(EAgentPermissionMode mode, FunctionCallContent call)
        => ApprovalRuleProbe.IsApprovedAsync(ApprovalModeMapper.BuildRules(mode, Root), call);

    [Fact]
    public async Task AutoEdit_ApprovesEditsInsideTheWorkspace()
    {
        Assert.True(await ApprovedAsync(EAgentPermissionMode.AutoEdit, Edit($"{Root}/src/A.cs")));
        Assert.True(await ApprovedAsync(EAgentPermissionMode.AutoEdit, Edit("src/A.cs"))); //相对路径解析到工作区
    }

    [Fact]
    public async Task AutoEdit_StillAsksForShell()
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.AutoEdit, Shell("rm -rf /")));
    }

    [Fact]
    public async Task ReadOnly_ApprovesNothingThatWrites()
    {
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Edit($"{Root}/src/A.cs")));
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, Shell("ls")));
    }

    /// <summary>档位现取：跑到一半调严，下一条调用就按新档审批，不等下一轮重新装配</summary>
    [Fact]
    public async Task ChangingTheMode_TakesEffectOnTheNextCall()
    {
        EAgentPermissionMode mode = EAgentPermissionMode.FullAuto;
        var rules = ApprovalModeMapper.BuildRules(() => mode, Root);

        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, Shell("dotnet build")));
        mode = EAgentPermissionMode.AutoEdit;
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, Shell("dotnet build")));
        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit($"{Root}/src/A.cs")));
        mode = EAgentPermissionMode.ReadOnly;
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, Edit($"{Root}/src/A.cs")));
    }

    [Fact]
    public async Task FullAuto_ApprovesShellAndInsideWorkspaceWrites()
    {
        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, Shell("dotnet build")));
        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, Edit($"{Root}/src/A.cs")));
    }

    /// <summary>
    /// 越界写入是贯穿三档的硬规则，<b>完全自动档也不例外</b>：用户点一次现成的"本会话允许"
    /// 之后框架自己会整会话放行，所以这一下只会被问一次。
    /// 定时任务是代码写死 AutoEdit 的，用户没机会为它选档——那种无人值守的场合，
    /// 这一条就是工作区外唯一的拦阻。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task OutOfWorkspaceWrite_IsNeverAutoApproved(EAgentPermissionMode mode)
    {
        Assert.False(await ApprovedAsync(mode, Edit("/etc/hosts")));
        Assert.False(await ApprovedAsync(mode, Edit($"{Root}/../outside/A.cs"))); //回溯出去也算越界
    }

    /// <summary>判据保守：没有工作目录可比、无从判定时一律要审批，不能让畸形参数悄悄越界落盘</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task UnjudgeableWrite_IsNeverAutoApproved(EAgentPermissionMode mode)
    {
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(
            ApprovalModeMapper.BuildRules(mode), Edit("/etc/hosts")));
    }

    /// <summary>
    /// 没有路径的写调用什么都写不了（filePath 必填，绑定当场失败）：框架解析不了参数 JSON 时交来的就是它。
    /// 拦下只会弹空卡、批了照样报错，放行让模型立刻拿到报错
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task WriteWithoutAPath_IsNotHeldForApproval(EAgentPermissionMode mode)
    {
        Assert.True(await ApprovedAsync(mode, new FunctionCallContent("c3", "Edit", null)));
        Assert.True(await ApprovedAsync(mode, new FunctionCallContent("c5", "Edit",
            new Dictionary<string, object?> { ["edits"] = "[]" })));
        Assert.False(await ApprovedAsync(EAgentPermissionMode.ReadOnly, new FunctionCallContent("c6", "Edit", null)));
    }

    /// <summary>越界判定只认写工具：读工具压根没包审批，不该被这条规则连带影响</summary>
    [Fact]
    public async Task FullAuto_StillApprovesNonWriteToolsAnywhere()
    {
        FunctionCallContent read = new("c4", "Read",
            new Dictionary<string, object?> { ["filePath"] = "/etc/hosts" });

        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, read));
    }

    /// <summary>
    /// 会话自己的产出房间视为界内：测试脚本与中间文件有地方去，就不必为它们弹审批。
    /// 只认这一间——兄弟会话的房间仍是界外，无豁免时(缺省参数)老行为不变
    /// </summary>
    /// <param name="mode">需要审批写工具的两档；只读档连房间也不放行</param>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task OwnOutputRoom_IsTreatedAsInside(EAgentPermissionMode mode)
    {
        const string room = "/tmp/uiharu-data/Agent/Workspaces/ws/12345678";

        Task<bool> ApprovedInRoomAsync(FunctionCallContent call) =>
            ApprovalRuleProbe.IsApprovedAsync(
                ApprovalModeMapper.BuildRules(mode, Root, approvedWriteRoot: room), call);

        Assert.True(await ApprovedInRoomAsync(Edit($"{room}/test.py")));
        Assert.True(await ApprovedInRoomAsync(Edit($"{room}/sub/dir/out.png")));
        Assert.False(await ApprovedInRoomAsync(Edit("/tmp/uiharu-data/Agent/Workspaces/ws/87654321/x.py"))); //兄弟房间仍问
        Assert.False(await ApprovedInRoomAsync(Edit("/etc/hosts"))); //房间之外仍问
    }

    /// <summary>
    /// 草稿目录简写按房间展开再判界：与工具执行落到同一处。
    /// 用 .. 走出房间的、没有房间可展开的，都照旧要问
    /// </summary>
    [Fact]
    public async Task DraftShorthand_IsJudgedWhereItActuallyLands()
    {
        const string room = "/tmp/uiharu-data/Agent/Workspaces/ws/12345678";

        Task<bool> ApprovedAsync(FunctionCallContent call, string approvedWriteRoot) =>
            ApprovalRuleProbe.IsApprovedAsync(
                ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit, Root, approvedWriteRoot: approvedWriteRoot),
                call);

        Assert.True(await ApprovedAsync(Edit("$DRAFT/probe.py"), room));
        Assert.True(await ApprovedAsync(Edit("$env:DRAFT/sub/out.png"), room));
        Assert.False(await ApprovedAsync(Edit("$DRAFT/../87654321/x.py"), room)); //走进兄弟房间
        Assert.False(await ApprovedAsync(Edit("$DRAFT/probe.py"), approvedWriteRoot: "")); //没有房间可展开
    }

    /// <summary>只读档连自己的房间也不放行：那一档什么写工具都不批</summary>
    [Fact]
    public async Task ReadOnly_StillDeniesWritesIntoOwnRoom()
    {
        const string room = "/tmp/uiharu-data/Agent/Workspaces/ws/12345678";

        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(
            ApprovalModeMapper.BuildRules(EAgentPermissionMode.ReadOnly, Root, approvedWriteRoot: room),
            Edit($"{room}/test.py")));
    }

    /// <summary>
    /// 记忆目录：<b>任何权限档可写</b>(ADR 0028)——记忆是 agent 的工作台，
    /// 只读/计划档也得能记笔记。范围只认 Memory/ 这一格，兄弟工作区的记忆仍要审批。
    /// </summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task MemoryWrite_IsApprovedInEveryMode(EAgentPermissionMode mode)
    {
        const string memory = "/tmp/uiharu-data/Agent/Workspaces/ws/Memory";

        Task<bool> ApprovedAsync(FunctionCallContent call) =>
            ApprovalRuleProbe.IsApprovedAsync(
                ApprovalModeMapper.BuildRules(mode, Root, memoryWriteRoot: memory), call);

        Assert.True(await ApprovedAsync(Edit($"{memory}/decisions.md")));
        Assert.True(await ApprovedAsync(Edit($"{memory}/sub/notes.md")));
        // 兄弟工作区的记忆仍问；工作区与房间之外(绝对越界)也仍问
        Assert.False(await ApprovedAsync(Edit("/tmp/uiharu-data/Agent/Workspaces/ws2/Memory/x.md")));
        Assert.False(await ApprovedAsync(Edit("/etc/hosts")));
    }

    /// <summary>删记忆走 shell：只读与自动编辑档仍需审批——记忆目录不在 shell 预授权里(ADR 0028)</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.ReadOnly)]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    public async Task MemoryDeletion_ViaShell_StillNeedsApproval(EAgentPermissionMode mode)
    {
        const string memory = "/tmp/uiharu-data/Agent/Workspaces/ws/Memory";

        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(
            ApprovalModeMapper.BuildRules(mode, Root, memoryWriteRoot: memory),
            Shell($"rm {memory}/decisions.md")));
    }
}
