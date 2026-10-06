using System.Net;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Models.Downloads;

/// <summary>
/// 仓库详情：量化按大小列、推荐项预选、同名冲突禁用、受限仓库就地要令牌
/// </summary>
public class ModelRepoDetailDataTests : IDisposable
{
    private const long GiB = 1L << 30;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-detail-{Guid.NewGuid():N}");
    private readonly RecordingMessageService _messages = new();
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));
    private IReadOnlyDictionary<string, string> _localModels = new Dictionary<string, string>();
    private int _openSettingsCount;
    private Func<(EEngineEnsureState, VersionInfo?, DownloadJob?)> _ensureEngine =
        () => (EEngineEnsureState.AlreadyInstalled, null, null);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private static readonly ModelRepoFile[] RepoFiles =
    [
        new("m-Q8_0.gguf", 8 * GiB, null),
        new("m-Q4_K_M.gguf", 5 * GiB, null),
        new("m-F16-00001-of-00002.gguf", 8 * GiB, null),
        new("m-F16-00002-of-00002.gguf", 8 * GiB, null),
        new("mmproj-F16.gguf", GiB / 2, null),
        new("README.md", 10, null)
    ];

    private ModelRepoDetailData Create(IModelSource source) => new(new ModelDownloadContext(_messages,
        new DownloadQueueViewData(_queue, action => action()), () => Task.CompletedTask, () => _openSettingsCount++)
    {
        Queue = _queue,
        Downloader = new ModelRepoDownloader(_queue),
        ModelRoot = () => _root,
        LocalModels = () => _localModels,
        EnsureEngine = () => Task.FromResult(_ensureEngine()),
        DeviceInfo = () => new RuntimeDeviceInfo(16 * GiB, 8 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now)
    }, source, "owner/repo");

    [Fact]
    public async Task Rows_AreSortedBySize_WithRecommendedPreselected()
    {
        ModelRepoDetailData detail = Create(new FakeModelSource { Files = RepoFiles });
        await detail.LoadAsync();

        Assert.Equal(["Q4_K_M", "Q8_0", "F16"], detail.Rows.Select(x => x.Label));
        ModelQuantRowData recommended = Assert.Single(detail.Rows, x => x.IsRecommended);
        Assert.Equal("Q4_K_M", recommended.Label);
        Assert.True(recommended.IsSelected);
        Assert.Equal("F16", detail.Projector?.Quantization);
        Assert.Equal("Error", detail.Rows.Last().RiskTag); //16G + 投影 + KV 超过 16G 的 85%
        Assert.All(detail.Rows, x => Assert.Equal(EQuantDownloadState.NotDownloaded, x.Status.State));
    }

    [Fact]
    public async Task SameNameElsewhere_DisablesThatRow()
    {
        _localModels = new Dictionary<string, string> { ["m-Q8_0"] = "/elsewhere/m-Q8_0.gguf" };
        ModelRepoDetailData detail = Create(new FakeModelSource { Files = RepoFiles });
        await detail.LoadAsync();

        ModelQuantRowData row = detail.Rows.Single(x => x.Label == "Q8_0");
        Assert.Equal(EQuantDownloadState.NameConflict, row.Status.State);
        Assert.False(row.CanDownload);
        Assert.Contains("/elsewhere/m-Q8_0.gguf", row.ConflictTip);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GatedRepository_AsksForTokenInPlace(HttpStatusCode code)
    {
        ModelRepoDetailData detail = Create(new FakeModelSource
        {
            ListError = new HttpRequestException("gated", null, code)
        });
        await detail.LoadAsync();

        Assert.True(detail.NeedsToken);
        Assert.Null(detail.ErrorText);
        Assert.Empty(_messages.Dialogs);
        detail.OpenSourceSettingsCommand.Execute(null);
        Assert.Equal(1, _openSettingsCount);
    }

    [Fact]
    public async Task Download_EnqueuesQuantAndProjector_IntoRepoDirectory()
    {
        ModelRepoDetailData detail = Create(new FakeModelSource { Files = RepoFiles });
        await detail.LoadAsync();

        await detail.DownloadCommand.ExecuteAsync(detail.Rows[0]);

        Assert.Equal(
        [
            Path.Combine(_root, "owner", "repo", "m-Q4_K_M.gguf"),
            Path.Combine(_root, "owner", "repo", "mmproj-F16.gguf")
        ], _queue.Jobs.Select(x => x.Request.DestinationPath));
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
    }

    [Fact]
    public async Task NoEngine_EngineIsQueuedBeforeTheModel_AndUserIsTold()
    {
        string enginePath = Path.Combine(_root, "Engine", "llama-b1.zip");
        VersionInfo engine = new() { Name = "llama-b1.zip" };
        _ensureEngine = () => (EEngineEnsureState.Queued, engine,
            _queue.Enqueue(engine.Name, new DownloadRequest(new Uri("http://127.0.0.1:1/e.zip"), enginePath)));
        ModelRepoDetailData detail = Create(new FakeModelSource { Files = RepoFiles });
        await detail.LoadAsync();

        await detail.DownloadCommand.ExecuteAsync(detail.Rows[0]);

        Assert.Equal(enginePath, _queue.Jobs[0].Request.DestinationPath);
        Assert.Equal(3, _queue.Jobs.Count);
        Assert.Contains(_messages.Notifications, x => x.Message.Contains("llama-b1.zip"));
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
    }

    [Fact]
    public async Task EngineCheckFails_ModelStillDownloads()
    {
        _ensureEngine = () => throw new HttpRequestException("rate limited");
        ModelRepoDetailData detail = Create(new FakeModelSource { Files = RepoFiles });
        await detail.LoadAsync();

        await detail.DownloadCommand.ExecuteAsync(detail.Rows[0]);

        Assert.Equal(2, _queue.Jobs.Count);
        Assert.Contains(_messages.Notifications, x => x.Severity == MessageSeverity.Warning);
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
    }
}
