using System.Security.Cryptography;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 按量化排队：落到 作者/仓库 目录、每个文件下完记清单、全齐了才报完成
/// </summary>
public class ModelRepoDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-repo-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(64 * 1024);
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));

    public void Dispose()
    {
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        Thread.Sleep(100);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public async Task ShardsAndProjector_LandInRepoDirectory_AndCompleteOnce()
    {
        using TestHttpServer server = new(_payload);
        string sha = Convert.ToHexStringLower(SHA256.HashData(_payload));
        ModelRepoQuant quant = new("m-Q4_K_M", "Q4_K_M",
        [
            new ModelRepoFile("Q4_K_M/m-Q4_K_M-00001-of-00002.gguf", _payload.Length, sha),
            new ModelRepoFile("Q4_K_M/m-Q4_K_M-00002-of-00002.gguf", _payload.Length, null)
        ]);
        ModelRepoQuant projector = new("mmproj-F16", "F16", [new ModelRepoFile("mmproj-F16.gguf", _payload.Length, sha)]);
        int completedCount = 0;

        IReadOnlyList<DownloadJob> jobs = new ModelRepoDownloader(_queue).Enqueue(new FakeSource(server.Url), "owner/repo",
            _root, quant, projector, () =>
            {
                Interlocked.Increment(ref completedCount);
                return Task.CompletedTask;
            });

        Assert.Equal(3, jobs.Count);
        await WaitFor(() => jobs.All(x => x.State == EDownloadJobState.Completed));
        string directory = Path.Combine(_root, "owner", "repo");
        Assert.True(File.Exists(Path.Combine(directory, "m-Q4_K_M-00002-of-00002.gguf")));
        Assert.Equal(1, completedCount);

        ModelManifest manifest = ModelManifest.TryLoad(directory)!;
        Assert.Equal("fake", manifest.Source);
        Assert.Equal("owner/repo", manifest.Repository);
        ManifestModel model = Assert.Single(manifest.Models);
        Assert.Equal(["m-Q4_K_M-00001-of-00002.gguf", "m-Q4_K_M-00002-of-00002.gguf"], model.Files.Select(x => x.Path));
        Assert.Equal(sha, model.Files[0].Sha256);
        Assert.Equal("", model.Files[1].Sha256);
        Assert.Equal("mmproj-F16.gguf", model.Projector);
        Assert.Equal("mmproj-F16.gguf", Assert.Single(manifest.Projectors).Path);
    }

    [Fact]
    public async Task ExistingProjector_IsNotDownloadedAgain_ButStillPaired()
    {
        using TestHttpServer server = new(_payload);
        string directory = ModelRepoDownloader.RepoDirectory(_root, "owner/repo");
        Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "mmproj-F16.gguf"), _payload,
            TestContext.Current.CancellationToken);
        ModelRepoQuant quant = new("m-Q8_0", "Q8_0", [new ModelRepoFile("m-Q8_0.gguf", _payload.Length, null)]);
        ModelRepoQuant projector = new("mmproj-F16", "F16", [new ModelRepoFile("mmproj-F16.gguf", _payload.Length, null)]);
        bool completed = false;

        IReadOnlyList<DownloadJob> jobs = new ModelRepoDownloader(_queue).Enqueue(new FakeSource(server.Url), "owner/repo",
            _root, quant, projector, () =>
            {
                completed = true;
                return Task.CompletedTask;
            });

        Assert.Single(jobs);
        await WaitFor(() => completed);
        Assert.Equal("mmproj-F16.gguf", ModelManifest.TryLoad(directory)!.FindModel("m-Q8_0.gguf")!.Projector);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "condition not met within 10s");
    }

    private sealed class FakeSource(Uri url) : IModelSource
    {
        public string Id => "fake";

        public Task<IReadOnlyList<ModelRepoSummary>> SearchAsync(string query, int limit, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken token) =>
            throw new NotSupportedException();

        public Task<string?> GetReadmeAsync(string repository, CancellationToken token) => Task.FromResult<string?>(null);

        public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath) =>
            new(url, destinationPath, 1, file.Sha256);
    }
}
