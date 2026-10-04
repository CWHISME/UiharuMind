using System.Diagnostics;
using Microsoft.Agents.AI.Tools.Shell;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Assembly;
using UiharuMind.Core.AI.Execution.Tools.BackgroundTasks;

namespace UiharuMind.Core.Tests.Agent;

/// <summary>
/// 后台任务跑的是真进程：只认进程本身退出（不陪后台子进程等管道）、到点连进程树收掉、
/// 结果经送达口交出去且注册表随之摘掉
/// </summary>
public class BackgroundTaskTests
{
    private static readonly string ShellBinary = ResolveShell();

    private static string ResolveShell()
    {
        LocalShellExecutor executor = ShellExecutorFactory.Create(Path.GetTempPath(), null);
        string binary = executor.ResolvedShellBinary;
        executor.DisposeAsync().AsTask().GetAwaiter().GetResult();
        return binary;
    }

    private static BackgroundTaskLaunch NewLaunch() =>
        new(ShellBinary, Path.GetTempPath(), ShellExecutorFactory.BuildEnvironment(null),
            Directory.CreateTempSubdirectory("bgtask").FullName);

    internal static async Task<BackgroundTaskOutcome> RunAsync(string command, TimeSpan maxRuntime)
    {
        RecordingSink sink = new();
        BackgroundTask task = BackgroundTaskRegistry.Start("owner", command, "test", maxRuntime, NewLaunch(), sink);
        return await sink.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task ExitedTask_ReportsExitCodeAndLogsOutput()
    {
        BackgroundTaskOutcome outcome = await RunAsync("echo hello; echo oops 1>&2; exit 3", TimeSpan.FromMinutes(1));

        Assert.Equal(EBackgroundTaskEnd.Exited, outcome.End);
        Assert.Equal(3, outcome.ExitCode);
        string log = await File.ReadAllTextAsync(outcome.Task.LogPath, TestContext.Current.CancellationToken);
        Assert.Contains("hello", log);
        Assert.Contains("oops", log); //stderr 一并进日志
        Assert.DoesNotContain(outcome.Task, BackgroundTaskRegistry.RunningOf("owner"));
    }

    [Fact]
    public async Task TaskPastItsTimeLimit_IsStopped()
    {
        Stopwatch watch = Stopwatch.StartNew();
        BackgroundTaskOutcome outcome = await RunAsync("sleep 30", TimeSpan.FromSeconds(1));

        Assert.Equal(EBackgroundTaskEnd.TimeLimit, outcome.End);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"没按时限收掉，等了 {watch.Elapsed}");
    }

    /// <summary>
    /// 命令自己又往后台丢了个子进程（它继承着输出管道）：Shell 工具会陪它等到管道关闭，这里只认进程本身
    /// </summary>
    [Fact]
    public async Task ChildHoldingThePipe_DoesNotDelayCompletion()
    {
        Stopwatch watch = Stopwatch.StartNew();
        BackgroundTaskOutcome outcome = await RunAsync("sleep 15 & echo started", TimeSpan.FromMinutes(1));

        Assert.Equal(EBackgroundTaskEnd.Exited, outcome.End);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(8), $"陪子进程等了管道，等了 {watch.Elapsed}");
    }

    [Fact]
    public async Task StoppedTask_EndsAsStopped()
    {
        RecordingSink sink = new();
        BackgroundTask task = BackgroundTaskRegistry.Start("owner", "sleep 30", "test", TimeSpan.FromMinutes(1),
            NewLaunch(), sink);

        Assert.True(BackgroundTaskRegistry.Stop(task.Id));

        BackgroundTaskOutcome outcome = await sink.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(EBackgroundTaskEnd.Stopped, outcome.End);
        Assert.False(BackgroundTaskRegistry.Stop(task.Id)); //已经结束的停不了
    }

    /// <summary>
    /// 壳已经退出、正等后台子进程放开管道的那几秒里点停止：它是自己跑完的，不能记成被叫停
    /// </summary>
    [Fact]
    public async Task StopDuringDrain_KeepsTheNaturalExit()
    {
        RecordingSink sink = new();
        BackgroundTask task = BackgroundTaskRegistry.Start("owner", "sleep 5 & echo hi", "test",
            TimeSpan.FromMinutes(1), NewLaunch(), sink);
        await Task.Delay(500, TestContext.Current.CancellationToken); //壳早已退出,还在等管道收尾

        BackgroundTaskRegistry.Stop(task.Id);

        BackgroundTaskOutcome outcome = await sink.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(10),
            TestContext.Current.CancellationToken);
        Assert.Equal(EBackgroundTaskEnd.Exited, outcome.End);
    }

    /// <summary>
    /// 退出收尾：在跑的记「被中止」，已跑完但还没送达的按真实结局落；之后不再接新任务
    /// </summary>
    [Fact]
    public async Task Shutdown_SettlesRunningAndUndelivered_ThenRefusesNewTasks()
    {
        try
        {
            RecordingSink running = new();
            BackgroundTaskRegistry.Start("owner", "sleep 30", "running", TimeSpan.FromMinutes(1), NewLaunch(), running);
            HeldSink held = new(); //模拟「会话一直忙、结果一直没落进去」
            BackgroundTaskRegistry.Start("owner", "echo done", "finished", TimeSpan.FromMinutes(1), NewLaunch(), held);
            await held.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            BackgroundTaskRegistry.SettleAllForShutdown();

            Assert.Equal(EBackgroundTaskEnd.AppExit, (await running.Delivered.Task).End);
            Assert.Equal(EBackgroundTaskEnd.Exited, Assert.Single(held.OnShutdown).End);
            Assert.Throws<InvalidOperationException>(() => BackgroundTaskRegistry.Start("owner", "echo late", "late",
                TimeSpan.FromMinutes(1), NewLaunch(), new RecordingSink()));
        }
        finally
        {
            BackgroundTaskRegistry.ResetShutdownForTests();
        }
    }

    [Fact]
    public async Task ReportFence_OutgrowsBackticksInTheOutput()
    {
        BackgroundTaskOutcome outcome = await RunAsync("true", TimeSpan.FromMinutes(1));

        string text = BackgroundTaskReport.BuildText(outcome, "```\ninner\n```");

        Assert.Contains("````\n```\ninner\n```\n````", text);
    }

    [Fact]
    public async Task ToolReturnsAtOnce_WithIdAndLogPath()
    {
        RecordingSink sink = new();
        AIFunction tool = BackgroundTaskTool.Create("owner", NewLaunch(), sink);

        Stopwatch watch = Stopwatch.StartNew();
        string? result = (await tool.InvokeAsync(new AIFunctionArguments
        {
            ["command"] = "sleep 2",
            ["description"] = "slow one",
        }, TestContext.Current.CancellationToken))?.ToString();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(1), $"工具没有立即返回，等了 {watch.Elapsed}");
        Assert.IsAssignableFrom<ApprovalRequiredAIFunction>(tool);
        Assert.Contains("Started background task", result);
        Assert.Contains(".log", result);
        BackgroundTaskOutcome outcome = await sink.Delivered.Task.WaitAsync(TimeSpan.FromSeconds(20),
            TestContext.Current.CancellationToken);
        Assert.Equal("slow one", outcome.Task.Description);
    }

    [Theory]
    [InlineData(EBackgroundTaskEnd.Exited, "退出码 0")]
    [InlineData(EBackgroundTaskEnd.TimeLimit, "时间上限")]
    [InlineData(EBackgroundTaskEnd.Stopped, "被用户叫停")]
    [InlineData(EBackgroundTaskEnd.AppExit, "应用退出")]
    public async Task ReportText_SaysHowItEndedAndThatItIsNotTheUser(EBackgroundTaskEnd end, string expected)
    {
        BackgroundTaskOutcome ran = await RunAsync("echo done", TimeSpan.FromMinutes(1));
        BackgroundTaskOutcome outcome = ran with { End = end, ExitCode = 0 };

        string text = BackgroundTaskReport.BuildText(outcome, "last line");

        Assert.Contains(expected, text);
        Assert.Contains("不是用户的回复", text);
        Assert.Contains(outcome.Task.LogPath, text);
        Assert.Contains("last line", text);
        Assert.True(ChatMessageAnnotations.IsBackgroundTaskReport(BackgroundTaskReport.BuildMessage(outcome)));
    }

    /// <summary>后台任务也是执行一条命令：会话里「记住同类命令」放行的，起后台任务时同样放行</summary>
    [Fact]
    public async Task SessionShellRule_AppliesToBackgroundTasks()
    {
        var rules = ApprovalModeMapper.BuildRules(EAgentPermissionMode.AutoEdit,
            sessionShellApprovalSource: () => ["dotnet build*"]);

        FunctionCallContent build = new("c1", BackgroundTaskTool.ToolName,
            new Dictionary<string, object?> { ["command"] = "dotnet build -c Release" });
        FunctionCallContent push = new("c2", BackgroundTaskTool.ToolName,
            new Dictionary<string, object?> { ["command"] = "git push" });

        Assert.True(await ApprovalRuleProbe.IsApprovedAsync(rules, build));
        Assert.False(await ApprovalRuleProbe.IsApprovedAsync(rules, push));
    }

    // 送达一直不完成：结果停在「已结束、未送达」
    private sealed class HeldSink : IBackgroundTaskReportSink
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public List<BackgroundTaskOutcome> OnShutdown { get; } = [];

        public Task DeliverAsync(BackgroundTaskOutcome outcome)
        {
            Arrived.TrySetResult();
            return new TaskCompletionSource().Task;
        }

        public void DeliverOnShutdown(BackgroundTaskOutcome outcome) => OnShutdown.Add(outcome);
    }

    private sealed class RecordingSink : IBackgroundTaskReportSink
    {
        public TaskCompletionSource<BackgroundTaskOutcome> Delivered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task DeliverAsync(BackgroundTaskOutcome outcome)
        {
            Delivered.TrySetResult(outcome);
            return Task.CompletedTask;
        }

        public void DeliverOnShutdown(BackgroundTaskOutcome outcome) => Delivered.TrySetResult(outcome);
    }
}
