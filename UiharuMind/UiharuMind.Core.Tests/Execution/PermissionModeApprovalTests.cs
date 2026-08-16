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

    /// <summary>判据保守：无从判定时一律要审批，不能让畸形参数悄悄越界落盘</summary>
    [Theory]
    [InlineData(EAgentPermissionMode.AutoEdit)]
    [InlineData(EAgentPermissionMode.FullAuto)]
    public async Task UnjudgeableWrite_IsNeverAutoApproved(EAgentPermissionMode mode)
    {
        FunctionCallContent noPath = new("c3", "Edit", new Dictionary<string, object?>());
        Assert.False(await ApprovedAsync(mode, noPath));

        // 没有工作目录可比 → 同样无从判定
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(
            ApprovalModeMapper.BuildRules(mode), Edit("/etc/hosts")));
    }

    /// <summary>越界判定只认写工具：读工具压根没包审批，不该被这条规则连带影响</summary>
    [Fact]
    public async Task FullAuto_StillApprovesNonWriteToolsAnywhere()
    {
        FunctionCallContent read = new("c4", "Read",
            new Dictionary<string, object?> { ["filePath"] = "/etc/hosts" });

        Assert.True(await ApprovedAsync(EAgentPermissionMode.FullAuto, read));
    }
}
