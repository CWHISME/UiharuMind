using System.Security.Cryptography;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 断点续传下载器，对着本机真 HTTP 服务跑。
/// 要守住的是：中断后接着下而不是重来、换了地址不拼两边的数据、校验不过不留半成品。
/// </summary>
public class FileDownloaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-dl-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(5 * 1024 * 1024 + 123);
    private readonly FileDownloader _downloader = new(new HttpClient());

    public FileDownloaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    private string Sha256 => Convert.ToHexStringLower(SHA256.HashData(_payload));
    private string Target => Path.Combine(_directory, "model.gguf");

    [Theory]
    [InlineData(1)]
    [InlineData(8)]
    public async Task Downloads_AndVerifies(int segments)
    {
        using TestHttpServer server = new(_payload);

        await _downloader.DownloadAsync(new DownloadRequest(server.Url, Target, segments, Sha256),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target, TestContext.Current.CancellationToken));
        Assert.Equal(segments == 1 ? 1 : 5, server.RangeRequests - 1); //减去探测那次；5MB 最多切 5 块
        Assert.Empty(Directory.GetFiles(_directory, "*.part*"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public async Task Interrupted_ResumesInsteadOfStartingOver(int segments)
    {
        using TestHttpServer server = new(_payload) { BytesPerSecond = 4 * 1024 * 1024 };
        using CancellationTokenSource cts = new();
        SyncProgress progress = new(p =>
        {
            if (p.ReceivedBytes > _payload.Length / 2) cts.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _downloader.DownloadAsync(new DownloadRequest(server.Url, Target, segments), progress, cts.Token));
        long servedBeforeResume = server.BytesServed;
        server.BytesPerSecond = 0;

        await _downloader.DownloadAsync(new DownloadRequest(server.Url, Target, segments, Sha256),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target, TestContext.Current.CancellationToken));
        Assert.True(server.BytesServed - servedBeforeResume < _payload.Length * 0.75,
            $"resume re-downloaded {server.BytesServed - servedBeforeResume} bytes");
    }

    [Fact]
    public async Task ChangedUrl_StartsOver()
    {
        using TestHttpServer server = new(_payload) { BytesPerSecond = 4 * 1024 * 1024 };
        using CancellationTokenSource cts = new();
        SyncProgress progress = new(p =>
        {
            if (p.ReceivedBytes > _payload.Length / 2) cts.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _downloader.DownloadAsync(new DownloadRequest(server.Url, Target), progress, cts.Token));
        long servedBefore = server.BytesServed;
        server.BytesPerSecond = 0;

        Uri mirror = new(server.Url, "?mirror=1");
        await _downloader.DownloadAsync(new DownloadRequest(mirror, Target, 1, Sha256),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.True(server.BytesServed - servedBefore >= _payload.Length);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ServerWithoutRange_StillDownloads()
    {
        using TestHttpServer server = new(_payload) { SupportsRange = false };

        await _downloader.DownloadAsync(new DownloadRequest(server.Url, Target, 8, Sha256),
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(_payload, await File.ReadAllBytesAsync(Target, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ChecksumMismatch_LeavesNothingBehind()
    {
        using TestHttpServer server = new(_payload);

        await Assert.ThrowsAsync<DownloadVerificationException>(() =>
            _downloader.DownloadAsync(new DownloadRequest(server.Url, Target, 4, new string('0', 64)),
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Empty(Directory.GetFiles(_directory));
    }

    // Progress<T> 投递到线程池，满载时取消会晚到、下载已经跑完；这里同步回调
    private sealed class SyncProgress(Action<DownloadProgress> report) : IProgress<DownloadProgress>
    {
        public void Report(DownloadProgress value) => report(value);
    }
}
