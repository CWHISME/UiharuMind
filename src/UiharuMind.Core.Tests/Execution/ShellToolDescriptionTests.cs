using System.Diagnostics;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 框架给 shell 工具的默认描述末尾写死了"用户会审批每一次调用"，自动档与预授权下并不成立。
/// 剥这句靠的是字符串替换，框架一改措辞就会静默失效——这条测试就是那道闸。
/// </summary>
public class ShellToolDescriptionTests
{
    [Fact]
    public async Task ShellTool_DoesNotClaimEveryCallIsApproved()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);

        AIFunction tool = ShellExecutorFactory.CreateTool(executor);

        Assert.Equal(CharacterRunnerFactory.ShellToolName, tool.Name);
        Assert.DoesNotContain("approves", tool.Description, StringComparison.OrdinalIgnoreCase);
        // 描述自写中文后,平台信息改成中文锚点("操作系统："),不再是框架默认的 "Shell:"
        Assert.Contains("操作系统：", tool.Description);
        Assert.IsAssignableFrom<ApprovalRequiredAIFunction>(tool);
    }

    /// <summary>
    /// 后台进程占着输出管道时，框架的调用不受自己的超时管、会一直挂着；兜底时限到点必须先返回说明
    /// </summary>
    [Fact]
    public async Task BackgroundJobHoldingThePipe_ReturnsNoticeAtTheGuardLimit()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);
        AIFunction guarded = new ShellHangGuardFunction(
            executor.AsAIFunction(CharacterRunnerFactory.ShellToolName), TimeSpan.FromSeconds(2));

        Stopwatch watch = Stopwatch.StartNew();
        // bash 的 `sleep 15 & echo started` 让后台进程占着管道;PowerShell 没有等价的
        // 后台继承管道写法(5.1 里 & 还是调用操作符),改用挂 15 秒的命令,同样触发兜底时限
        string command = OperatingSystem.IsWindows() ? "Start-Sleep 15" : "sleep 15 & echo started";
        object? result = await guarded.InvokeAsync(new AIFunctionArguments { ["command"] = command },
            TestContext.Current.CancellationToken);

        Assert.Equal(ShellHangGuardFunction.HangNotice, result?.ToString());
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"兜底没生效，等了 {watch.Elapsed}");
    }

    [Fact]
    public async Task OrdinaryCommand_PassesThroughTheGuard()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);
        AIFunction guarded = new ShellHangGuardFunction(
            executor.AsAIFunction(CharacterRunnerFactory.ShellToolName), TimeSpan.FromSeconds(30));

        object? result = await guarded.InvokeAsync(new AIFunctionArguments { ["command"] = "echo hello" },
            TestContext.Current.CancellationToken);

        Assert.Contains("hello", result?.ToString());
    }
}
