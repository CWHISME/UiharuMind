using System.Text;
using CliWrap;
using UiharuMind.Core.Core.Process;

namespace UiharuMind.Core.Tests.Core;

/// <summary>
/// 子进程随本进程一起死。本进程被强杀时内核会关掉生命线的写端——测试里让生命线结束就是同一回事
/// </summary>
public class ChildProcessBindingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-binding-{Guid.NewGuid():N}");

    public ChildProcessBindingTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task LifelineCut_KillsChild()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows binds through a job object");
        string pidFile = Path.Combine(_directory, "pid");
        TaskCompletionSource cut = new();
        PipeSource lifeline = PipeSource.Create(async (_, token) => await cut.Task.WaitAsync(token));

        CommandTask<CommandResult> run = ChildProcessBinding
            .Wrap("/bin/sh", ["-c", "echo $$ > \"$1\"; exec sleep 30", "child", pidFile], lifeline)
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync();
        int pid = await ReadPidAsync(pidFile);
        Assert.True(IsAlive(pid));

        cut.SetResult();
        await run.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(IsAlive(pid));
    }

    [Fact]
    public async Task ChildExitsOnItsOwn_ExitCodeAndArgumentsPassThrough()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "Windows binds through a job object");
        StringBuilder output = new();

        CommandResult result = await ChildProcessBinding
            .Wrap("/bin/sh", ["-c", "printf '%s|%s' \"$1\" \"$2\"; exit 7", "child", "a b", "it's"])
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output))
            .WithValidation(CommandResultValidation.None)
            .ExecuteAsync()
            .Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("a b|it's", output.ToString());
    }

    private static async Task<int> ReadPidAsync(string pidFile)
    {
        for (int i = 0; i < 100; i++)
        {
            if (File.Exists(pidFile) && int.TryParse(File.ReadAllText(pidFile).Trim(), out int pid)) return pid;
            await Task.Delay(50);
        }

        throw new TimeoutException("Child never wrote its pid.");
    }

    private static bool IsAlive(int pid)
    {
        try
        {
            using System.Diagnostics.Process process = System.Diagnostics.Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
