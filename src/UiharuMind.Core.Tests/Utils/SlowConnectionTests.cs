using System.Security.Cryptography;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.Tests.Utils;

/// <summary>
/// 长连接中途掉速：走代理时同一站点新开的连接常比卡住的那条快几倍，下载器要换连接而不是一直耗着；
/// 换了还一样慢说明是网络整体变慢，只换一次就认。请求序号：每个文件先一次探测，再是数据连接
/// </summary>
public class SlowConnectionTests : IDisposable
{
    private const int Fast = 4 * 1024 * 1024;
    private const int Slow = 256 * 1024;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-slow-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(1024 * 1024);
    private readonly FileDownloader _downloader = new(new HttpClient(),
        new SlowConnectionPolicy(TimeSpan.FromMilliseconds(150), 0.25, 10 * 1024, 50));

    public SlowConnectionTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task SlowConnection_IsReplaced_AndFileStaysIntact()
    {
        // 0/1：第一个文件正常速度，学到站点基准；2/3：第二个文件的数据连接卡成慢速；4：换的新连接
        using TestHttpServer server = new(_payload) { BytesPerSecondByRequest = i => i == 3 ? Slow : Fast };

        await Download(server, "a.gguf");
        await Download(server, "b.gguf");

        Assert.Equal(5, server.RangeRequests);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Path.Combine(_directory, "b.gguf"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WholeNetworkSlowsDown_ReconnectsOnce_ThenAccepts()
    {
        using TestHttpServer server = new(_payload) { BytesPerSecondByRequest = i => i < 2 ? Fast : Slow };

        await Download(server, "a.gguf");
        await Download(server, "b.gguf");

        Assert.Equal(5, server.RangeRequests); // b：探测 + 慢连接 + 换了仍慢就接着下
        Assert.Equal(_payload, await File.ReadAllBytesAsync(Path.Combine(_directory, "b.gguf"),
            TestContext.Current.CancellationToken));
    }

    private async Task Download(TestHttpServer server, string name)
    {
        string sha = Convert.ToHexStringLower(SHA256.HashData(_payload));
        await _downloader.DownloadAsync(new DownloadRequest(server.Url, Path.Combine(_directory, name), 1, sha),
            cancellationToken: TestContext.Current.CancellationToken);
    }
}
