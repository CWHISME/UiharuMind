using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// shell 自杀拦截（2026-10-12 实测：模型为换档案重起连杀四次，最后一刀正中自己）：
/// 杀进程动词落在应用自己头上时直接拒绝、不进执行；良性命令（查进程、别的进程名）照常放行；
/// 两把 shell 工具的链里都有它，且最外层仍是审批（框架靠最外层认需审批）。
/// </summary>
public class ShellSelfKillGuardTests
{
    public static TheoryData<string> SelfKillCommands => new()
    {
        """pkill -f "UiharuMind.Desktop" 2>/dev/null; sleep 1""",
        "pkill -9 -f UiharuMind.Desktop",
        "killall -9 UiharuMind.Desktop",
        $"kill {Environment.ProcessId}",
        $"kill -9 {Environment.ProcessId}",
        $"taskkill /PID {Environment.ProcessId} /F",
        "taskkill /IM UiharuMind.Desktop.exe /F",
        "Stop-Process -Name UiharuMind.Desktop",
        "kill $PPID",
        "kill -9 ${PPID}",
    };

    public static TheoryData<string> BenignCommands => new()
    {
        """ps aux | grep -i "UiharuMind" | grep -v grep""",
        "ls src/UiharuMind.CLI/bin/Debug/net10.0/UiharuMind.CLI",
        "dotnet build src/UiharuMind.CLI/UiharuMind.CLI.csproj -v q -nologo",
        """pkill -f "some-test-server" """,
        $"kill {Environment.ProcessId + 1}",
        "echo hello",
        "skill --help", //含 kill 三个字母但不是杀进程动词，\b 必须挡住
        "",
    };

    [Theory]
    [MemberData(nameof(SelfKillCommands))]
    public async Task SelfKillCommand_IsRefusedWithoutInvokingInner(string command)
    {
        (AIFunction guarded, Func<bool> invoked) = NewGuarded();

        object? result = await guarded.InvokeAsync(new AIFunctionArguments { ["command"] = command },
            TestContext.Current.CancellationToken);

        Assert.StartsWith(ShellSelfKillGuardFunction.RefusalPrefix, result?.ToString());
        Assert.False(invoked());
    }

    /// <summary>heredoc 写脚本时正文提到 pkill 仍拒绝（正文可能被执行），但文案点出命中段并指向 Write</summary>
    [Fact]
    public async Task HeredocBody_IsRefusedWithHitSnippetAndWriteHint()
    {
        (AIFunction guarded, Func<bool> invoked) = NewGuarded();
        string command = "cat > restart.sh <<'EOF'\npkill -f UiharuMind.Desktop\nopen -a UiharuMind\nEOF";

        object? result = await guarded.InvokeAsync(new AIFunctionArguments { ["command"] = command },
            TestContext.Current.CancellationToken);

        string text = result?.ToString() ?? string.Empty;
        Assert.Contains("「pkill -f UiharuMind.Desktop」", text);
        Assert.Contains("`Write`", text);
        Assert.False(invoked());
    }

    [Theory]
    [MemberData(nameof(BenignCommands))]
    public async Task BenignCommand_PassesThrough(string command)
    {
        (AIFunction guarded, Func<bool> invoked) = NewGuarded();

        object? result = await guarded.InvokeAsync(new AIFunctionArguments { ["command"] = command },
            TestContext.Current.CancellationToken);

        Assert.StartsWith("inner:", result?.ToString());
        Assert.True(invoked());
    }

    /// <summary>
    /// 接线不断：真装配的 Shell 工具走完整链（审批包裹在外，直接调用不弹卡），自杀命令到执行层被拒绝。
    /// 探针用 <c>kill -0</c>（只问进程在不在，不发信号）：拦截失效时它也能安全执行，测试只会红、不会杀测试进程。
    /// </summary>
    [Fact]
    public async Task ShellTool_RefusesSelfKillThroughTheFullChain()
    {
        await using LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);
        AIFunction tool = ShellExecutorFactory.CreateTool(executor);

        Assert.IsAssignableFrom<ApprovalRequiredAIFunction>(tool); //最外层仍是审批（框架靠它认需审批）
        object? result = await tool.InvokeAsync(
            new AIFunctionArguments { ["command"] = $"kill -0 {Environment.ProcessId}" },
            TestContext.Current.CancellationToken);

        Assert.StartsWith(ShellSelfKillGuardFunction.RefusalPrefix, result?.ToString());
    }

    /// <summary>同上，后台任务工具：拒绝时连任务都不建</summary>
    [Fact]
    public async Task BackgroundTaskTool_RefusesSelfKillThroughTheFullChain()
    {
        AIFunction tool = BackgroundTaskTool.Create("owner", NewLaunch(), new NullSink());

        Assert.IsAssignableFrom<ApprovalRequiredAIFunction>(tool);
        object? result = await tool.InvokeAsync(
            new AIFunctionArguments
            {
                ["command"] = $"kill -0 {Environment.ProcessId}",
                ["description"] = "test",
            },
            TestContext.Current.CancellationToken);

        Assert.StartsWith(ShellSelfKillGuardFunction.RefusalPrefix, result?.ToString());
    }

    private static (AIFunction Guarded, Func<bool> Invoked) NewGuarded()
    {
        bool invoked = false;
        AIFunction inner = AIFunctionFactory.Create((string command) =>
        {
            invoked = true;
            return "inner:" + command;
        }, "Shell", "test");
        return (new ShellSelfKillGuardFunction(inner), () => invoked);
    }

    private static BackgroundTaskLaunch NewLaunch()
    {
        LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);
        string binary = executor.ResolvedShellBinary;
        executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return new BackgroundTaskLaunch(binary, Path.GetTempPath(),
            ShellExecutorFactory.BuildEnvironment(null), Path.GetTempPath());
    }

    private sealed class NullSink : IBackgroundTaskReportSink
    {
        public Task DeliverAsync(BackgroundTaskOutcome outcome) => Task.CompletedTask;

        public void DeliverOnShutdown(BackgroundTaskOutcome outcome)
        {
        }
    }
}
