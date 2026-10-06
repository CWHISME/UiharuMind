using System.Security.Cryptography;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 没有引擎时自动排包：已装或已排就不再排，连点只排一次，下完装好并选中
/// </summary>
public class LLamaCppEngineInstallerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-engine-{Guid.NewGuid():N}");
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));
    private readonly List<VersionInfo> _installed = [];
    private readonly List<VersionInfo> _selected = [];
    private IReadOnlyList<VersionInfo> _local = [];
    private IReadOnlyList<VersionInfo> _remote = [];
    private int _pullCount;

    public void Dispose()
    {
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        Thread.Sleep(100);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    // 推荐变体随本机平台而定，挑一个本机认作推荐的包名
    private static string? RecommendedSuffix() =>
        new[] { "-bin-macos-arm64", "-bin-macos-x64", "-bin-win-vulkan-x64", "-bin-win-vulkan-arm64", "-bin-ubuntu-vulkan-x64", "-bin-ubuntu-vulkan-arm64" }
            .FirstOrDefault(x => LLamaCppVariants.IsRecommended("llama-b1" + x + ".zip"));

    private VersionInfo Remote(string build, string suffix, Uri? url = null) => new()
    {
        Name = $"llama-{build}{suffix}.zip",
        Version = BuildVersion(build),
        DownloadUrl = (url ?? new Uri("http://127.0.0.1:1/x.zip")).ToString(),
        PackageFilePath = Path.Combine(_root, "LLamaCpp", $"llama-{build}{suffix}.zip")
    };

    private static Version BuildVersion(string build) => new(int.Parse(build[1..]), 0);

    private LLamaCppEngineInstaller Create() => new(_queue, _root,
        () => Task.FromResult(_local),
        async () =>
        {
            Interlocked.Increment(ref _pullCount);
            await Task.Delay(50);
            return _remote;
        },
        (version, _) =>
        {
            _installed.Add(version);
            return Task.CompletedTask;
        },
        _selected.Add);

    [Fact]
    public async Task AlreadyInstalled_QueuesNothing()
    {
        _local = [new VersionInfo { Name = "local", IsInstalled = true }];

        var result = await Create().EnsureQueuedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EEngineEnsureState.AlreadyInstalled, result.State);
        Assert.Empty(_queue.Jobs);
        Assert.Equal(0, _pullCount);
    }

    [Fact]
    public async Task NoEngine_QueuesLatestRecommended_Once()
    {
        string? suffix = RecommendedSuffix();
        Assert.SkipWhen(suffix == null, "本机平台没有推荐变体");
        _remote = [Remote("b100", suffix!), Remote("b200", suffix!), Remote("b300", "-bin-unknown-platform")];
        LLamaCppEngineInstaller installer = Create();

        var results = await Task.WhenAll(installer.EnsureQueuedAsync(), installer.EnsureQueuedAsync());

        Assert.Single(results, x => x.State == EEngineEnsureState.Queued);
        Assert.Single(results, x => x.State == EEngineEnsureState.AlreadyQueued);
        Assert.Equal($"llama-b200{suffix}.zip", results.Single(x => x.Version != null).Version!.Name);
        Assert.Same(_queue.Jobs[0], results.Single(x => x.Job != null).Job);
        Assert.Single(_queue.Jobs);
    }

    [Fact]
    public async Task NoRecommendedPackage_ReportsIt()
    {
        _remote = [Remote("b300", "-bin-unknown-platform")];

        var result = await Create().EnsureQueuedAsync(TestContext.Current.CancellationToken);

        Assert.Equal(EEngineEnsureState.NoPackage, result.State);
        Assert.Empty(_queue.Jobs);
    }

    [Fact]
    public async Task DownloadedPackage_IsInstalledThenSelected()
    {
        using TestHttpServer server = new(RandomNumberGenerator.GetBytes(4096));
        VersionInfo version = Remote("b100", "-bin-any", server.Url);
        LLamaCppEngineInstaller installer = Create();
        VersionInfo? installedEvent = null;
        installer.Installed += x => installedEvent = x;

        DownloadJob job = installer.Enqueue(version, selectWhenInstalled: true);
        for (int i = 0; i < 250 && job.State != EDownloadJobState.Completed; i++) await Task.Delay(20);

        Assert.Equal(EDownloadJobState.Completed, job.State);
        Assert.Equal([version], _installed);
        Assert.Equal([version], _selected);
        Assert.Same(version, installedEvent);
    }

    [Fact]
    public async Task Update_IsNewerRecommendedPackage_AndClearsOnceInstalled()
    {
        string? suffix = RecommendedSuffix();
        if (suffix == null) return;
        VersionInfo installed = Remote("b100", suffix);
        installed.IsInstalled = true;
        VersionInfo newer = Remote("b120", suffix);
        _remote = [installed, Remote("b110", suffix), newer, Remote("b130", "-bin-unknown-variant")];
        LLamaCppEngineInstaller installer = Create();
        int changes = 0;
        installer.AvailableUpdateChanged += () => changes++;

        Assert.Same(newer, await installer.CheckForUpdateAsync());
        await installer.InstallAsync(newer);

        Assert.Null(installer.AvailableUpdate);
        Assert.Equal(2, changes);
    }

    [Fact]
    public void Update_NotOfferedWithoutAnInstalledEngine_OrWhenUpToDate()
    {
        string? suffix = RecommendedSuffix();
        if (suffix == null) return;
        VersionInfo installed = Remote("b120", suffix);
        installed.IsInstalled = true;

        Assert.Null(LLamaCppEngineInstaller.PickUpdate([Remote("b120", suffix)]));
        Assert.Null(LLamaCppEngineInstaller.PickUpdate([installed, Remote("b110", suffix)]));
    }
}
