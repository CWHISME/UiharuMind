using UiharuMind.Core.AI.Execution.Tools.Scheduler;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Instances;

namespace UiharuMind.Core.Tests.Chat;

public sealed class CrossProcessLockTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"uiharu-lock-{Guid.NewGuid():N}", "x.lock");

    public void Dispose()
    {
        string directory = Path.GetDirectoryName(_path)!;
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }

    [Fact]
    public void SecondHolder_IsRefused_UntilTheFirstReleases()
    {
        IDisposable? first = ExclusiveFileLock.TryAcquire(_path);
        Assert.NotNull(first);
        Assert.Null(ExclusiveFileLock.TryAcquire(_path));

        first!.Dispose();
        using IDisposable? second = ExclusiveFileLock.TryAcquire(_path);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task SecondaryScheduler_RefusesNewTasks_AndLeavesTheFileAlone()
    {
        DateTime before = File.Exists(AppPaths.Data.ScheduledAgentTasks)
            ? File.GetLastWriteTimeUtc(AppPaths.Data.ScheduledAgentTasks)
            : DateTime.MinValue;
        using InProcessSchedulerBackend secondary = new(primary: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => secondary.ScheduleAsync(new ScheduledAgentTask
        {
            DisplayName = "t",
            Prompt = "p",
            FireAt = DateTimeOffset.Now.AddHours(1),
        }));

        DateTime after = File.Exists(AppPaths.Data.ScheduledAgentTasks)
            ? File.GetLastWriteTimeUtc(AppPaths.Data.ScheduledAgentTasks)
            : DateTime.MinValue;
        Assert.Equal(before, after);
    }
}
