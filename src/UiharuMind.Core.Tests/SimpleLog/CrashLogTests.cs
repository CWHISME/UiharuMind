using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Tests.SimpleLog;

public class CrashLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-crashlog-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    [Fact]
    public void AppendKeepsEarlierCrashes()
    {
        CrashLog.Append(new InvalidOperationException("first"), _directory);
        CrashLog.Append(new InvalidOperationException("second"), _directory);

        string text = File.ReadAllText(Path.Combine(_directory, "Crash.txt"));
        Assert.Contains("first", text);
        Assert.Contains("second", text);
    }

    [Fact]
    public void AppendMovesOversizedFileAside()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(Path.Combine(_directory, "Crash.txt"), new string('x', 1024 * 1024 + 1));

        CrashLog.Append(new InvalidOperationException("fresh"), _directory);

        Assert.True(File.Exists(Path.Combine(_directory, "Crash.old.txt")));
        Assert.Contains("fresh", File.ReadAllText(Path.Combine(_directory, "Crash.txt")));
    }
}
