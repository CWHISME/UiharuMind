using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.AudioModels;
using UiharuMind.Features.Models.AudioModels.Online;
using UiharuMind.Features.Models.ChatModels;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.ImageModels;
using UiharuMind.Features.Models.Usage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 三个已装模型页签公共的那部分：「获取模型」「添加远程模型」两条只有一个出处（<see cref="ModelFolderBar"/>），
/// 三个页签同一文案、同一顺序（各页签自己的按钮排在它前面）；右上角的资源读数与页签行上下居中
/// </summary>
[Collection(HeadlessCollection.Name)]
public class ModelPageToolbarTests : IDisposable
{
    private const long GiB = 1L << 30;

    private readonly RecordingMessageService _messages = new();
    private readonly FakeAudioProvider _synthesizer = new();
    private readonly RecordingAudioPlayer _player = new();
    private TestAudioEngine? _audio;
    private Window? _window;

    public void Dispose()
    {
        HeadlessUi.Run(() => _window?.Close());
        _audio?.Dispose();
    }

    [Fact]
    public void InstalledTabs_ShareTheTwoCommonButtons_AndReadoutCentersOnTabs()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = Open();
            ModelPage page = (ModelPage)_window!.Content!;
            await Settle();

            string getModels = Loc.Text(LangKey.ModelPageGetModelsBtn);
            string addRemote = Loc.Text(LangKey.ModelListAddRemote);
            (int Tab, Type View, string[] OwnActions)[] tabs =
            [
                (0, typeof(ChatModelListView), []),
                (1, typeof(ImageModelListView), [Loc.Text(LangKey.ImageDrawOpen)]),
                (2, typeof(AudioModelListView), [])
            ];

            foreach ((int tab, Type view, string[] ownActions) in tabs)
            {
                data.SelectedTabIndex = tab;
                await Settle();
                Control list = (Control)page.GetVisualDescendants().Single(x => x.GetType() == view);
                ModelFolderBar bar = list.GetVisualDescendants().OfType<ModelFolderBar>().Single();
                Capture($"model-toolbar-{tab}");

                Button shared = bar.FindControl<Button>("GetModels")!;
                Button remote = bar.FindControl<Button>("AddRemote")!;
                Assert.Equal(getModels, Text(shared));
                Assert.Equal(addRemote, Text(remote));

                // 从左到右：目录条自己的（更改、打开、重新扫描）→ 这一页签自己的 → 共用的两条。
                // 共用的恒在最右且顺序固定，各页签的按钮只能排在它前面
                Button[] visible = bar.GetVisualDescendants().OfType<Button>()
                    .Where(x => x.IsEffectivelyVisible).OrderBy(x => CenterIn(bar, x).X).ToArray();
                int sharedAt = Array.IndexOf(visible, shared);
                Assert.True(sharedAt >= ownActions.Length);
                Assert.Equal(ownActions, visible.Skip(sharedAt - ownActions.Length).Take(ownActions.Length).Select(Text));
                Assert.Equal([remote], visible.Skip(sharedAt + 1));
            }

            // 读数在页签那一行、与页签文字上下居中，不再偏到内容上面
            TabStrip tabStrip = page.GetVisualDescendants().OfType<TabStrip>().Single();
            LocalUsageIndicator readout = page.GetVisualDescendants().OfType<LocalUsageIndicator>().Single();
            double offset = Math.Abs(CenterIn(page, tabStrip).Y - CenterIn(page, readout).Y);
            Assert.InRange(offset, 0, 1.5);
        });
    }

    private ModelPageData Open()
    {
        TestAudioEngine audio = new(scanModels: () => []);
        _audio = audio;
        string models = Path.Combine(Path.GetTempPath(), "uiharu-toolbar", "models");
        // 接了在线服务列表，语音页签的工具条才有一条「添加远程模型」
        AudioServiceSettingConfig services = new();
        services.ReplaceServices([new AudioServiceInfo { Name = "Edge TTS", Dialect = EAudioDialect.EdgeTts }]);
        AudioServiceListViewData online = new(
            new AudioRouter(() => _synthesizer, new AudioSettingConfig(), services, _ => _synthesizer,
                (task, _) => task == EAudioCapability.Tts),
            services, new ReadAloudService(_ => _synthesizer, _player, _messages), _messages,
            (_, _) => Task.FromResult<AudioServiceInfo?>(null));
        ModelPageData data = new(_messages, page =>
        {
            ModelDownloadContext context = new(_messages, page.EnsureDownloadQueue(), () => Task.CompletedTask, () => { })
            {
                CreateSource = () => new FakeModelSource(),
                Queue = audio.Queue,
                Downloader = new ModelRepoDownloader(audio.Queue),
                ModelRoot = () => models,
                LocalModels = () => new Dictionary<string, string>(),
                DeviceInfo = () => new RuntimeDeviceInfo(16 * GiB, 8 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now)
            };
            GgufMarketHooks hooks = new()
            {
                EnsureEngine = () => Task.FromResult((EEngineEnsureState.AlreadyInstalled, (EnginePackage?)null, (DownloadJob?)null))
            };
            return new ModelDownloadPageData(context, [new GgufMarketData(context, hooks)]);
        }, audio.Queue, modelSources: [],
            createImageActions: _ => ImageModelListActions.None with { ModelRoot = models, OpenFolder = _ => { } },
            createAudioModels: page => new AudioModelListViewData(_messages, audio.Engine, audio.Store,
                (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
                new ReadAloudService(_ => _synthesizer, _player, _messages), _ => { },
                page.ShowAudioDownloads, () => { }, null, online,
                new AudioPreferencesViewData(new AudioSettingConfig())));
        _window = new Window { Width = 1000, Height = 680, Content = new ModelPage { DataContext = data } };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        return data;
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
        string dir = Environment.GetEnvironmentVariable("MODEL_DOWNLOAD_SHOTS_DIR");
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        _window!.UpdateLayout();
        using Bitmap? frame = Avalonia.Headless.HeadlessWindowExtensions.CaptureRenderedFrame(_window);
        frame?.Save(Path.Combine(dir, name + ".png"), new PngBitmapEncoderOptions());
    }

    private static string Text(Button button) =>
        button.GetVisualDescendants().OfType<TextBlock>().First().Text ?? "";

    /// <summary>控件的中心点，换算到 <paramref name="root"/> 的坐标系里再比</summary>
    private static Point CenterIn(Visual root, Visual visual)
    {
        Point topLeft = visual.TranslatePoint(new Point(0, 0), root)!.Value;
        Size size = visual.Bounds.Size;
        return new Point(topLeft.X + size.Width / 2, topLeft.Y + size.Height / 2);
    }
}
