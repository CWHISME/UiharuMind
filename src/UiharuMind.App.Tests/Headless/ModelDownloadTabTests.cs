using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.Downloads;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 「获取模型」页签：模型页首开不建、不搜；第一次选中才建并搜索。
/// 顺带出一张宽窗截图、一张窄窗截图（目录由 <c>MODEL_DOWNLOAD_SHOTS_DIR</c> 指定，默认系统临时目录）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ModelDownloadTabTests : IDisposable
{
    private const long GiB = 1L << 30;
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-tab-{Guid.NewGuid():N}");
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));
    private readonly RecordingMessageService _messages = new();
    private Window? _window;

    public void Dispose()
    {
        HeadlessUi.Run(() => _window?.Close());
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private const string SampleReadme = """
        ---
        license: apache-2.0
        base_model: Qwen/Qwen3-8B
        tags: [gguf]
        ---
        <!-- header start -->
        # Qwen3-8B GGUF

        Quantized with **llama.cpp**. See the [original model](https://huggingface.co/Qwen/Qwen3-8B).

        ## Which file should I choose?

        | Quant | Size | Notes |
        |---|---|---|
        | Q4_K_M | 5.0 GB | Recommended |
        | Q8_0 | 8.7 GB | Near lossless |

        ```bash
        llama-server -m Qwen3-8B-Q4_K_M.gguf --jinja
        ```

        > Thinking mode is on by default.
        """;

    private readonly FakeModelSource _source = new()
    {
        Readme = SampleReadme,
        SearchResults =
        [
            new ModelRepoSummary("unsloth/Qwen3-8B-GGUF", 1_820_000),
            new ModelRepoSummary("bartowski/google_gemma-3-4b-it-GGUF", 964_000),
            new ModelRepoSummary("Qwen/Qwen3-Embedding-0.6B-GGUF", 312_400),
            new ModelRepoSummary("ggml-org/SmolVLM-500M-Instruct-GGUF", 8_800)
        ],
        Files =
        [
            new ModelRepoFile("Qwen3-8B-Q2_K.gguf", 3_280_000_000, null),
            new ModelRepoFile("Qwen3-8B-Q4_K_M.gguf", 5_030_000_000, null),
            new ModelRepoFile("Qwen3-8B-Q5_K_M.gguf", 5_850_000_000, null),
            new ModelRepoFile("Qwen3-8B-Q8_0.gguf", 8_710_000_000, null),
            new ModelRepoFile("BF16/Qwen3-8B-BF16-00001-of-00002.gguf", 8_200_000_000, null),
            new ModelRepoFile("BF16/Qwen3-8B-BF16-00002-of-00002.gguf", 8_200_000_000, null),
            new ModelRepoFile("mmproj-F16.gguf", 870_000_000, null)
        ]
    };

    private ModelPageData CreatePage() => new(_messages, page => new ModelDownloadPageData(
        new ModelDownloadContext(_messages, page.EnsureDownloadQueue(), () => Task.CompletedTask, () => { })
        {
            CreateSource = () => _source,
            Queue = _queue,
            Downloader = new ModelRepoDownloader(_queue),
            ModelRoot = () => _root,
            LocalModels = () => new Dictionary<string, string> { ["Qwen3-8B-Q8_0"] = "/Models/Qwen3-8B-Q8_0.gguf" },
            EnsureEngine = () => Task.FromResult((EEngineEnsureState.AlreadyInstalled, (VersionInfo?)null, (DownloadJob?)null)),
        DeviceInfo = () => new RuntimeDeviceInfo(16 * GiB, 8 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now)
        }), _queue);

    [Fact]
    public void Tab_IsBuiltOnFirstSelection_NotOnPageOpen()
    {
        HeadlessUi.RunAsync(async () =>
        {
            // 截图语言（如 zh-hans），默认不动
            string? language = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_LANG");
            if (language != null) UiharuMind.Shared.Services.LocalizationManager.Instance.ApplyLanguage(language, false);
            ModelPageData data = CreatePage();
            ModelPage page = new() { DataContext = data };
            _window = new Window { Width = 1000, Height = 680, Content = page };
            _window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.Null(data.Downloads);
            Assert.Empty(page.GetVisualDescendants().OfType<ModelDownloadView>());
            Assert.Equal(0, _source.SearchCount);

            data.SelectedTabIndex = 2;
            await Settle();

            Assert.NotNull(data.Downloads);
            Assert.Single(page.GetVisualDescendants().OfType<ModelDownloadView>());
            Assert.Equal(1, _source.SearchCount);
            Assert.Equal(4, data.Downloads!.Results.Count);

            // 截图：打开一个仓库，排一个下载让下载区露出来
            data.Downloads.SelectedItem = data.Downloads.Results[0];
            await Settle();
            while (data.Downloads.Detail!.IsLoading) await Settle();
            await data.Downloads.Detail.DownloadCommand.ExecuteAsync(data.Downloads.Detail.Rows[0]);
            _queue.Pause(_queue.Jobs[0]);
            await Settle();
            Capture("model-download-wide");

            data.Downloads.Detail.ShowReadmeCommand.Execute(null);
            for (int i = 0; i < 10; i++) await Settle();
            Capture("model-download-readme");
            data.Downloads.Detail.ShowFilesCommand.Execute(null);

            _window.Width = 560;
            await Settle();
            Capture("model-download-narrow");
        });
    }

    [Fact]
    public void ShowDownloads_OpensTabWithSearchPrefilled_AndSearchesOnce()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = CreatePage();
            _window = new Window { Width = 1000, Height = 680, Content = new ModelPage { DataContext = data } };
            _window.Show();

            data.ShowDownloads("Embedding");
            await Settle();

            Assert.Equal(2, data.SelectedTabIndex);
            Assert.Equal("Embedding", data.Downloads!.SearchText);
            Assert.Equal(1, _source.SearchCount);
            Assert.Equal("Embedding", _source.LastQuery);
        });
    }

    private static async Task Settle()
    {
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
        }
    }

    private void Capture(string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        _window!.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(_window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"));
    }
}
