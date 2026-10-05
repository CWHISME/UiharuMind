using System.Text.Json;
using UiharuMind.Core.Core.Diagnostics;

namespace UiharuMind.Core.Tests.SimpleLog;

public class UncleanExitGuardTests : IDisposable
{
    private const int DeadPid = int.MaxValue - 7;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-unclean-{Guid.NewGuid():N}");
    private string Markers => Path.Combine(_root, "markers");
    private string Logs => Path.Combine(_root, "logs");
    private string Preserve => Path.Combine(_root, "crash");

    public UncleanExitGuardTests()
    {
        Directory.CreateDirectory(Markers);
        Directory.CreateDirectory(Logs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void DeadRunWithoutDebuggerPreservesItsLogOldestFirst()
    {
        File.WriteAllText(Path.Combine(Logs, "Log.2.txt"), "# session: aaaa1111\nolder\n");
        File.WriteAllText(Path.Combine(Logs, "Log.1.txt"), "# session: aaaa1111\nnewer\n");
        File.WriteAllText(Path.Combine(Logs, "Log.txt"), "# session: bbbb2222\nother run\n");
        WriteMarker(DeadPid, debugger: false, "aaaa1111");

        Begin(out List<UncleanExit> found);

        UncleanExit exit = Assert.Single(found);
        Assert.False(exit.DebuggerAttached);
        Assert.NotNull(exit.PreservedLog);
        string text = File.ReadAllText(exit.PreservedLog!);
        Assert.True(text.IndexOf("older", StringComparison.Ordinal) < text.IndexOf("newer", StringComparison.Ordinal));
        Assert.DoesNotContain("other run", text);
    }

    [Fact]
    public void DeadRunUnderDebuggerIsReportedButNotPreserved()
    {
        File.WriteAllText(Path.Combine(Logs, "Log.1.txt"), "# session: aaaa1111\nx\n");
        WriteMarker(DeadPid, debugger: true, "aaaa1111");

        Begin(out List<UncleanExit> found);

        UncleanExit exit = Assert.Single(found);
        Assert.True(exit.DebuggerAttached);
        Assert.Null(exit.PreservedLog);
        Assert.False(Directory.Exists(Preserve));
    }

    [Fact]
    public void CleanExitLeavesNothingForNextStart()
    {
        UncleanExitGuard guard = Begin(out _);
        guard.MarkClean();

        Begin(out List<UncleanExit> found);

        Assert.Empty(found);
    }

    [Fact]
    public void LiveInstanceIsNotTreatedAsUnclean()
    {
        // 第一次 Begin 写下的是本进程(活着)的标记,第二次 Begin 不该把它当成事故
        Begin(out _);

        Begin(out List<UncleanExit> found);

        Assert.Empty(found);
    }

    private UncleanExitGuard Begin(out List<UncleanExit> found) =>
        UncleanExitGuard.Begin(Markers, Logs, Preserve, "cccc3333", debuggerAttached: false, out found);

    private void WriteMarker(int pid, bool debugger, string session)
    {
        string json = JsonSerializer.Serialize(new
        {
            Pid = pid,
            ProcessStart = DateTime.Now.AddHours(-1),
            StartedAt = DateTime.Now.AddHours(-1),
            DebuggerAttached = debugger,
            LogSession = session
        });
        File.WriteAllText(Path.Combine(Markers, $"{pid}.json"), json);
    }
}
