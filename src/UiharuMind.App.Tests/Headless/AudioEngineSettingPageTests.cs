using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Settings.Audio;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 本地模型（语音）页在「引擎已装、本机有一个合成模型」时：模型下拉选中它，获取模型默认只列推荐包，
/// 展开全部后能看到要 eSpeak-ng 的包与它的提示。引擎是临时目录里造的，spec 内联，不碰网络
/// </summary>
[Collection(HeadlessCollection.Name)]
public class AudioEngineSettingPageTests : IDisposable
{
    private const string Spec = """
        {
          "family": "pocket_tts", "category": "tts", "status": "supported", "languages": ["en"],
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf" } },
          "packages": [
            { "id": "pocket_tts_english_q8_0", "display_name": "PocketTTS English Q8_0 GGUF", "format": "gguf",
              "precision": "q8_0", "target_directory": "PocketTTS-GGUF/english",
              "files": ["PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf"], "strip_prefix": "PocketTTS-GGUF/english" }
          ]
        }
        """;

    private const string EspeakSpec = """
        {
          "family": "kokoro_tts", "category": "tts", "status": "community", "languages": ["en-us", "zh"],
          "runtime": { "phonemizer": "espeak-ng" },
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf" } },
          "packages": [
            { "id": "kokoro_82m_q8_0", "display_name": "Kokoro 82M Q8_0 GGUF", "format": "gguf", "precision": "q8_0",
              "target_directory": "Kokoro-82M-GGUF", "files": ["Kokoro-82M-GGUF/kokoro-82m-q8_0.gguf"] }
          ]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-audio-page-{Guid.NewGuid():N}");
    private Window? _window;

    public void Dispose()
    {
        HeadlessUi.Run(() => _window?.Close());
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void InstalledEngine_ListsRecommendedPackages_AndLocalModel()
    {
        HeadlessUi.Run(() =>
        {
            AudioEngineSettingData data = CreateData();
            AudioEngineSettingView view = new(data);
            _window = new Window { Width = 960, Height = 900, Content = view };
            _window.Show();
            Settle(() => data.Catalog.Rows.Count > 0 && data.Catalog.Rows.All(x => x.SizeText != "—"));

            Assert.Equal("pocket-tts-english-q8_0", data.Models.SelectedTtsModel);
            Assert.False(data.Models.HasAsrModels);
            AudioPackageRowData pocket = Assert.Single(data.Catalog.Rows);
            Assert.True(pocket.IsRecommended);
            Assert.Equal("1 KB", pocket.SizeText.Replace(".0", "")); // 大小来自仓库列表

            data.Catalog.ShowAll = true;
            Settle(() => data.Catalog.Rows.Count == 2);
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Needs eSpeak-ng");
            Capture("audio-with-engine");
        });
    }

    private AudioEngineSettingData CreateData()
    {
        // 装好的引擎：版本目录里有 audiocpp_server 与 model_specs
        string version = Path.Combine(_root, "engines", "audio-v0.9.1-bin-macos-arm64-metal");
        Directory.CreateDirectory(Path.Combine(version, "model_specs"));
        File.WriteAllText(Path.Combine(version, AudioCppVersionManager.ServerExeName), "");
        File.WriteAllText(Path.Combine(version, "model_specs", "pocket_tts.json"), Spec);
        File.WriteAllText(Path.Combine(version, "model_specs", "kokoro_tts.json"), EspeakSpec);

        AudioCppEngine engine = new(new AudioCppSettingConfig(), Path.Combine(_root, "engines"),
            Path.Combine(_root, "server.json"),
            () => [new AudioCppModelEntry("pocket-tts-english-q8_0", "pocket_tts", "/m/p.gguf", "tts")]);
        engine.GetLocalVersionsAsync().GetAwaiter().GetResult();

        FakeModelSource source = new()
        {
            Files =
            [
                new ModelRepoFile("PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf", 1024, null),
                new ModelRepoFile("Kokoro-82M-GGUF/kokoro-82m-q8_0.gguf", 2048, null)
            ]
        };
        DownloadQueue queue = new(new FileDownloader(new HttpClient()));
        AudioCppPackageStore store = new(queue, () => Path.Combine(_root, "models"),
            package => new AudioCppPackageSource(source, package.Repository, null));
        return new AudioEngineSettingData(new RecordingMessageService(), engine, store, queue, _ => { });
    }

    private static void Settle(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "界面没在 2 秒内到位");
    }

    private void Capture(string name)
    {
        string? dir = Environment.GetEnvironmentVariable("SETTINGS_SHOTS_DIR");
        dir = string.IsNullOrWhiteSpace(dir) ? Path.Combine(Path.GetTempPath(), "uiharu-settings-shots") : dir;
        Directory.CreateDirectory(dir);
        _window!.UpdateLayout();
        using Bitmap? frame = _window.CaptureRenderedFrame();
        frame?.Save(Path.Combine(dir, $"{name}.png"), PngBitmapEncoderOptions.Default);
    }
}
