using System.Security.Cryptography;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 全局下载队列：一次只跑一项；暂停保留进度、取消不留残渣、失败可重试。
/// </summary>
public class DownloadQueueTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-queue-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(2 * 1024 * 1024);
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));

    public DownloadQueueTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        Thread.Sleep(100);
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task RunsOneJobAtATime()
    {
        using TestHttpServer server = new(_payload) { BytesPerSecond = 4 * 1024 * 1024 };

        DownloadJob first = _queue.Enqueue("a", Request(server, "a.bin"));
        DownloadJob second = _queue.Enqueue("b", Request(server, "b.bin"));
        await WaitFor(() => first.State == EDownloadJobState.Running);

        Assert.Equal(EDownloadJobState.Queued, second.State);
        server.BytesPerSecond = 0;
        await WaitFor(() => second.State == EDownloadJobState.Completed);
        Assert.Equal(EDownloadJobState.Completed, first.State);
    }

    [Fact]
    public async Task PauseThenResume_FinishesTheSameFile()
    {
        using TestHttpServer server = new(_payload) { BytesPerSecond = 2 * 1024 * 1024 };
        DownloadJob job = _queue.Enqueue("a", Request(server, "a.bin"));
        await WaitFor(() => job.Progress.ReceivedBytes > 0);

        _queue.Pause(job);
        await WaitFor(() => File.Exists(Path.Combine(_directory, "a.bin.part.json")));
        server.BytesPerSecond = 0;
        _queue.Resume(job);

        await WaitFor(() => job.State == EDownloadJobState.Completed);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Path.Combine(_directory, "a.bin"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Cancel_RemovesJobAndPartialFiles()
    {
        using TestHttpServer server = new(_payload) { BytesPerSecond = 1024 * 1024 };
        DownloadJob job = _queue.Enqueue("a", Request(server, "a.bin"));
        await WaitFor(() => job.Progress.ReceivedBytes > 0);

        _queue.Cancel(job);

        Assert.Empty(_queue.Jobs);
        await WaitFor(() => Directory.GetFiles(_directory).Length == 0);
    }

    [Fact]
    public async Task Failure_IsReported_AndResumeRetries()
    {
        using TestHttpServer server = new(_payload) { StatusCode = 503 };
        DownloadJob job = _queue.Enqueue("a", Request(server, "a.bin"));
        await WaitFor(() => job.State == EDownloadJobState.Failed);
        Assert.NotNull(job.Error);

        server.StatusCode = 200;
        _queue.Resume(job);

        await WaitFor(() => job.State == EDownloadJobState.Completed);
    }

    [Fact]
    public async Task OnDownloaded_RunsAfterTheFileLands_AndCountsAsPartOfTheJob()
    {
        using TestHttpServer server = new(_payload);
        string target = Path.Combine(_directory, "a.bin");
        bool fileExistedInCallback = false;

        DownloadJob job = _queue.Enqueue("a", new DownloadRequest(server.Url, target), _ =>
        {
            fileExistedInCallback = File.Exists(target);
            return Task.CompletedTask;
        });

        await WaitFor(() => job.State == EDownloadJobState.Completed);
        Assert.True(fileExistedInCallback);
    }

    private DownloadRequest Request(TestHttpServer server, string fileName) =>
        new(server.Url, Path.Combine(_directory, fileName));

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "condition not met within 10s");
    }
}
