using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.Downloads.Image;
using UiharuMind.Features.Models.ImageModels;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 本地生图的界面：「获取模型」的生图分类（家族、档位、共用组件只下一次、试画把装好的加进列表），
/// 生图模型页签里的本地条目，与画图窗口（存图、停止）。引擎、清单、模型源都是临时造的，不联网。
/// 顺带出截图（目录由 <c>MODEL_DOWNLOAD_SHOTS_DIR</c> 指定，默认系统临时目录）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ImageModelTabsTests : IDisposable
{
    private const long GiB = 1L << 30;
    private static readonly ImageCatalogComponent SharedEncoder = new("llm", "acme/encoder", "encoder-q4.gguf", 2 * GiB, null);

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-image-{Guid.NewGuid():N}");
    private readonly List<ImageModelInfo> _original = ImageModelSettingConfig.Current.Models;
    private readonly RecordingMessageService _messages = new();
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));
    private readonly List<ImageModelInfo?> _drawn = []; //打开画图窗口时选中的
    private SdCppEngine? _engine;
    private Window? _window;

    public ImageModelTabsTests()
    {
        ImageModelSettingConfig.Current.ReplaceModels([]);
    }

    private string ModelRoot => Path.Combine(_root, "models");

    public void Dispose()
    {
        HeadlessUi.Run(() => _window?.Close());
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        _engine?.Dispose();
        ImageModelSettingConfig.Current.ReplaceModels(_original);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Market_RecommendedFirst_MarksUntested_AndOffersEngine()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = Open(installEngine: false);
            data.ShowImageDownloads();
            ImageMarketData market = data.Downloads!.Market<ImageMarketData>();
            await Settle(() => !market.EnginePrompt.HasEngine);

            Assert.Equal(["Turbo", "Editor"], market.Families.Select(x => x.Title));
            Assert.True(market.Families[0].IsRecommended);
            Assert.False(market.Families[0].IsUntested);
            Assert.True(market.Families[1].IsUntested);
            Assert.Single(_window!.GetVisualDescendants().OfType<ImageMarketListView>());

            market.SearchText = "edit";
            Assert.Equal(["Editor"], market.Families.Select(x => x.Title));
            market.SearchText = "nothing-like-this";
            Assert.True(market.IsResultEmpty);
            market.SearchText = "";
            market.SelectedFilter = market.FilterOptions.Single(x => x.Editing == true);
            Assert.Equal(["Editor"], market.Families.Select(x => x.Title));
            market.SelectedFilter = market.FilterOptions.Single(x => x.Editing == false);
            Assert.Equal(["Turbo"], market.Families.Select(x => x.Title));
            market.SelectedFilter = market.FilterOptions[0];
            Capture("image-market");
        });
    }

    [Fact]
    public void Detail_InstalledTier_DrawAddsEntryOnce()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageCatalogFamily turbo = Catalog().Find("turbo")!;
            ImageCatalogTier lite = turbo.Tiers[0];
            LocalImageModelStore store = Store();
            foreach (ImageCatalogComponent component in lite.Components) WriteFile(store.PathOf(component));

            ModelPageData data = Open();
            data.ShowImageDownloads();
            ImageMarketData market = data.Downloads!.Market<ImageMarketData>();
            await Settle(() => market.EnginePrompt.HasEngine && market.Families.Count == 2);
            Assert.True(market.Families[0].IsInstalled);

            market.SelectedFamily = market.Families[0];
            ImageFamilyDetailData detail = market.Detail!;
            await Settle(() => !detail.IsLoading);
            Assert.True(detail.Rows[0].IsDownloaded);
            // 共用的编码器在、自己的主模型不在：算没下，不算下了一半
            Assert.Equal(EQuantDownloadState.NotDownloaded, detail.Rows[1].Status.State);
            Assert.True(detail.Rows[1].IsRecommended);
            Assert.Single(_window!.GetVisualDescendants().OfType<ImageFamilyDetailView>());
            Capture("image-family-detail");

            detail.DrawCommand.Execute(detail.Rows[0]);
            detail.DrawCommand.Execute(detail.Rows[0]);

            ImageModelInfo entry = Assert.Single(ImageModelSettingConfig.Current.Models);
            Assert.Equal("Turbo", entry.Name);
            Assert.Equal(("turbo", "lite"), (entry.Local!.Family, entry.Local.Tier));
            Assert.Equal([entry, entry], _drawn);
            Assert.Single(data.ImageModels.Items);
        });
    }

    [Fact]
    public void Download_QueuesOnlyMissingComponents()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageCatalogFamily turbo = Catalog().Find("turbo")!;
            WriteFile(Store().PathOf(SharedEncoder));

            ModelPageData data = Open();
            data.ShowImageDownloads();
            ImageMarketData market = data.Downloads!.Market<ImageMarketData>();
            await Settle(() => market.EnginePrompt.HasEngine && market.Families.Count == 2);
            market.SelectedFamily = market.Families[0];
            ImageFamilyDetailData detail = market.Detail!;

            await detail.DownloadCommand.ExecuteAsync(detail.Rows[1]);

            DownloadJob job = Assert.Single(_queue.Jobs);
            Assert.EndsWith(turbo.Tiers[1].Components[0].File, job.Request.DestinationPath);
            Assert.False(detail.Rows[1].CanDownload);
            Assert.Empty(ImageModelSettingConfig.Current.Models); //没到齐不进列表
        });
    }

    [Fact]
    public void ImageTab_LocalEntry_ShowsBadgeAndTry()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageCatalogFamily turbo = Catalog().Find("turbo")!;
            ImageModelSettingConfig.Current.ReplaceModels(
            [
                LocalImageModels.Create(turbo, turbo.Tiers[0], "Turbo"),
                new ImageModelInfo { Name = "remote", Endpoint = "https://x", ModelId = "m" }
            ]);
            ModelPageData data = Open();
            data.ShowImageModels();
            await Settle(() => data.ImageModels.Items.Count == 2);

            Assert.True(data.ImageModels.Items[0].IsLocal);
            Assert.False(data.ImageModels.Items[1].IsLocal);
            Assert.Single(_window!.GetVisualDescendants().OfType<ImageModelListView>());
            Capture("image-models-tab");

            data.ImageModels.DrawCommand.Execute(data.ImageModels.Items[0]);
            Assert.Equal([ImageModelSettingConfig.Current.Models[0]], _drawn);
        });
    }

    [Fact]
    public void Draw_SavesIntoOutputDirectory_AndStopCancels()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageModelInfo model = new() { Name = "remote", Endpoint = "https://x", ModelId = "m" };
            TaskCompletionSource<ImageGenerationReport> pending = new();
            string output = Path.Combine(_root, "images");
            byte[] png = TinyPng();
            int calls = 0;
            ImageDrawViewData draw = new([model], null, (_, request, _, token) =>
            {
                calls++;
                Assert.Equal("a red fox", request.Prompt);
                Assert.Equal(new ImageAspectRatio(16, 9), request.AspectRatio);
                if (calls == 1) return Task.FromResult(new ImageGenerationReport { Images = [new GeneratedImage(png, "image/png")] });
                token.Register(() => pending.TrySetCanceled(token));
                return pending.Task;
            }, new ImageDrawActions(_ => { }, _ => Task.CompletedTask, _ => { }, _ => { }, () => Task.FromResult<IReadOnlyList<string>>([])), output);

            Assert.False(draw.GenerateCommand.CanExecute(null));
            draw.Prompt = "  a red fox ";
            draw.SelectedRatio = draw.Ratios.Single(x => x.Value == new ImageAspectRatio(16, 9));
            await draw.GenerateCommand.ExecuteAsync(null);

            Assert.True(draw.HasResult);
            Assert.StartsWith(output, draw.ResultPath);
            Assert.EndsWith("-a-red-fox.png", draw.ResultPath);
            Assert.NotNull(draw.Image);

            Task second = draw.GenerateCommand.ExecuteAsync(null);
            Assert.True(draw.IsGenerating);
            draw.StopCommand.Execute(null);
            await second;
            Assert.False(draw.IsGenerating);
            Assert.False(draw.HasResult);
            draw.Dispose();
        });
    }

    [Fact]
    public void Draw_MemoryRisk_ShowsTheShortfall_AndLoadAnywayRetries()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageModelInfo model = new() { Name = "local", Local = new LocalImageModel() };
            List<bool> attempts = [];
            ImageDrawViewData draw = new([model], null, (_, request, _, _) =>
            {
                attempts.Add(request.AllowMemoryRisk);
                return Task.FromResult(request.AllowMemoryRisk
                    ? new ImageGenerationReport { Images = [new GeneratedImage(TinyPng(), "image/png")] }
                    : new ImageGenerationReport
                    {
                        Failure = new ImageFailure(EImageFailureKind.Unavailable, "low memory")
                        {
                            MemoryShortfall = new EngineMemoryShortfall(12L << 30, 4L << 30)
                        }
                    });
            }, new ImageDrawActions(_ => { }, _ => Task.CompletedTask, _ => { }, _ => { }, () => Task.FromResult<IReadOnlyList<string>>([])),
                Path.Combine(_root, "images")) { Prompt = "fox" };

            await draw.GenerateCommand.ExecuteAsync(null);
            Assert.True(draw.HasMemoryRisk);
            Assert.Contains("12.0", draw.MemoryRiskText);
            Assert.False(draw.HasResult);

            await draw.GenerateAnywayCommand.ExecuteAsync(null);
            Assert.Equal([false, true], attempts);
            Assert.False(draw.HasMemoryRisk);
            Assert.True(draw.HasResult);
            draw.Dispose();
        });
    }

    [Fact]
    public void Draw_WithReferences_EditsOnlyWithEditingModel_AndCanContinueFromResult()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageModelInfo painter = new() { Name = "painter", Endpoint = "https://x", ModelId = "m", SupportsEditing = false };
            ImageModelInfo editor = new() { Name = "editor", Endpoint = "https://x", ModelId = "e" };
            string output = Path.Combine(_root, "images");
            Directory.CreateDirectory(_root);
            byte[] png = TinyPng();
            string reference = Path.Combine(_root, "ref.png");
            File.WriteAllBytes(reference, png);
            List<ImageRequest> requests = [];
            ImageDrawViewData draw = new([painter, editor], painter, (_, request, _, _) =>
                {
                    requests.Add(request);
                    return Task.FromResult(new ImageGenerationReport { Images = [new GeneratedImage(png, "image/png")] });
                },
                new ImageDrawActions(_ => { }, _ => Task.CompletedTask, _ => { }, _ => { },
                    () => Task.FromResult<IReadOnlyList<string>>([reference, Path.Combine(_root, "notes.txt")])),
                output);
            draw.Prompt = "make it blue";
            Assert.Null(draw.SelectedRatio.Value); //默认自动：有参考图跟第一张的形状

            await draw.AddReferencesCommand.ExecuteAsync(null);
            Assert.Equal(reference, Assert.Single(draw.References.Items).Path); //不是图的跳过
            Assert.False(draw.GenerateCommand.CanExecute(null)); //选的模型不能改图
            Assert.Contains("painter", draw.CannotEditHint);

            draw.SelectedModel = editor;
            Assert.Equal("", draw.CannotEditHint);
            await draw.GenerateCommand.ExecuteAsync(null);
            ImageRequest edit = Assert.Single(requests);
            Assert.True(edit.IsEdit);
            Assert.Null(edit.AspectRatio);

            await draw.EditResultCommand.ExecuteAsync(null);
            Assert.Equal([draw.ResultPath!, reference], draw.References.Items.Select(x => x.Path));

            draw.References.RemoveCommand.Execute(draw.References.Items[0]);
            draw.References.RemoveCommand.Execute(draw.References.Items[0]);
            Assert.False(draw.References.HasItems);
            draw.SelectedModel = painter;
            Assert.True(draw.GenerateCommand.CanExecute(null));
            draw.Dispose();
        });
    }

    [Fact]
    public void Draw_MemoryRisk_OffersToFreeOtherLocalModels_ThenLoadsNormally()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ImageModelInfo model = new() { Name = "local", Local = new LocalImageModel() };
            int calls = 0;
            int released = 0;
            List<bool> attempts = [];
            ImageDrawViewData draw = new([model], null, (_, request, _, _) =>
            {
                attempts.Add(request.AllowMemoryRisk);
                return Task.FromResult(++calls > 1
                    ? new ImageGenerationReport { Images = [new GeneratedImage(TinyPng(), "image/png")] }
                    : new ImageGenerationReport
                    {
                        Failure = new ImageFailure(EImageFailureKind.Unavailable, "low memory")
                        {
                            MemoryShortfall = new EngineMemoryShortfall(12L << 30, 4L << 30)
                        }
                    });
            }, new ImageDrawActions(_ => { }, _ => Task.CompletedTask, _ => { }, _ => { }, () => Task.FromResult<IReadOnlyList<string>>([])),
                Path.Combine(_root, "images"), () => [new LocalMemoryHolder("qwen", () => released++)],
                TimeSpan.Zero) { Prompt = "fox" };

            await draw.GenerateCommand.ExecuteAsync(null);
            Assert.True(draw.CanReleaseOthers);
            Assert.Contains("qwen", draw.ReleaseOthersText);

            await draw.ReleaseOthersAndGenerateCommand.ExecuteAsync(null);
            Assert.Equal(1, released);
            Assert.Equal([false, false], attempts); //放掉之后照常查内存，不硬起
            Assert.True(draw.HasResult);
            Assert.False(draw.CanReleaseOthers);
            draw.Dispose();
        });
    }

    private ModelPageData Open(bool installEngine = true)
    {
        string engines = Path.Combine(_root, "engines");
        Directory.CreateDirectory(engines);
        if (installEngine)
        {
            string version = Path.Combine(engines, "master-945-a1ded76-bin-Darwin-macOS-26.6.2-arm64");
            Directory.CreateDirectory(version);
            File.WriteAllText(Path.Combine(version, SdCppVersionManager.ExecutableName), "");
        }

        _engine = new SdCppEngine(new SdCppSettingConfig(), engines, _queue);
        ImageModelCatalog catalog = Catalog();
        LocalImageModelStore store = Store();
        ModelPageData data = new(_messages, page =>
        {
            ModelDownloadContext context = new(_messages, page.EnsureDownloadQueue(), () => Task.CompletedTask, () => { })
            {
                CreateSource = () => new FakeModelSource(),
                Queue = _queue,
                Downloader = new ModelRepoDownloader(_queue),
                ModelRoot = () => ModelRoot,
                LocalModels = () => new Dictionary<string, string>(),
                DeviceInfo = () => new RuntimeDeviceInfo(16 * GiB, 8 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now)
            };
            ImageMarketServices image = new(_engine.Distribution, store, catalog, () => ImageModelSettingConfig.Current.Models,
                page.ImageModels.Append, _drawn.Add, _ => { });
            return new ModelDownloadPageData(context, [new ImageMarketData(context, image)]);
        }, _queue, createImageActions: _ => ImageModelListActions.None with { OpenDraw = _drawn.Add });
        _window = new Window { Width = 1000, Height = 680, Content = new ModelPage { DataContext = data } };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        return data;
    }

    private LocalImageModelStore Store() => new(new ModelRepoDownloader(_queue), () => ModelRoot,
        () => new FakeModelSource(), () => ImageModelSettingConfig.Current.Models);

    // 故意把推荐的排在后面，看列表是不是按推荐重排
    private static ImageModelCatalog Catalog()
    {
        ImageCatalogSampling sampling = new(8, 1f);
        ImageCatalogFamily editor = new("editor", "Editor",
            new Dictionary<string, string> { ["en"] = "Edits images.", ["zh-hans"] = "改图。" },
            "https://example.com/editor", false, false, true, 1024, sampling,
            [new ImageCatalogTier("balanced", [new("diffusion-model", "acme/editor", "editor-q4.gguf", 6 * GiB, null), SharedEncoder])
                { Recommended = true }]);
        ImageCatalogFamily turbo = new("turbo", "Turbo",
            new Dictionary<string, string> { ["en"] = "Fast text to image.", ["zh-hans"] = "快速文生图。" },
            "https://example.com/turbo", true, true, false, 1024, sampling,
            [
                new ImageCatalogTier("lite", [new("diffusion-model", "acme/turbo", "turbo-q3.gguf", 3 * GiB, null), SharedEncoder]),
                new ImageCatalogTier("balanced", [new("diffusion-model", "acme/turbo", "turbo-q8.gguf", 7 * GiB, null), SharedEncoder])
                    { Recommended = true }
            ]);
        return new ImageModelCatalog("master-945-a1ded76", [editor, turbo]);
    }

    private static void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[16]);
    }

    private static byte[] TinyPng()
    {
        using WriteableBitmap bitmap = new(new PixelSize(2, 2), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using MemoryStream stream = new();
        bitmap.Save(stream, PngBitmapEncoderOptions.Default);
        return stream.ToArray();
    }

    private static async Task Settle(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }

        await Task.Delay(20);
        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "界面没在 2 秒内到位");
    }

    private void Capture(string name)
    {
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR") ?? Path.GetTempPath();
        Directory.CreateDirectory(dir);
        _window!.UpdateLayout();
        using Bitmap frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(_window)
                             ?? throw new InvalidOperationException("空帧");
        frame.Save(Path.Combine(dir, name + ".png"), PngBitmapEncoderOptions.Default);
    }
}
