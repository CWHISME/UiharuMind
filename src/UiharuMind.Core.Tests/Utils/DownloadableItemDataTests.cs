using System.Security.Cryptography;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 下载列表的一行要跟得上别处排进全局队列的同一个包（「安装引擎」按钮、模型下载自动装引擎），
/// 而且列表清掉时只停自己开的任务，不能把别人的下载暂停
/// </summary>
public class DownloadableItemDataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-item-{Guid.NewGuid():N}");
    private DownloadJob? _job;

    public DownloadableItemDataTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (_job != null) DownloadQueue.Shared.Cancel(_job);
        Thread.Sleep(100);
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task JobQueuedElsewhere_IsFollowed_AndNotPausedOnDispose()
    {
        using TestHttpServer server = new(RandomNumberGenerator.GetBytes(256 * 1024)) { BytesPerSecond = 32 * 1024 };
        string package = Path.Combine(_directory, "engine.zip");
        DownloadableItemData item = new(new ManagedVersionPackage
        {
            Name = "engine",
            DownloadUrl = server.Url.ToString(),
            PackageFilePath = package
        });

        _job = DownloadQueue.Shared.Enqueue("engine", new DownloadRequest(server.Url, package));
        for (int i = 0; i < 100 && item.DownloadProgress <= 0; i++) await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.True(item.IsDownloading);
        Assert.True(item.DownloadProgress > 0, "没跟上别处排的任务的进度");

        item.Dispose();
        Assert.Equal(EDownloadJobState.Running, _job.State);
    }
}
