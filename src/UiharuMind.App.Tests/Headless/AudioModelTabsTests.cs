using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Features.Audio;
using UiharuMind.Features.Models;
using UiharuMind.Features.Models.AudioModels;
using UiharuMind.Features.Models.AudioModels.Online;
using UiharuMind.Features.Models.AudioModels.Voices;
using UiharuMind.Features.Models.Downloads;
using UiharuMind.Features.Models.Downloads.Audio;

namespace UiharuMind.App.Tests.Headless;

/// <summary>
/// 语音模型的两处：「获取模型」的语音分类（按家族列 spec 里的包、下载、设默认），
/// 「语音模型」页签（本机装了的，设默认、选音色、删除）。引擎、spec、模型源都是临时造的，不联网。
/// 顺带出截图（目录由 <c>MODEL_DOWNLOAD_SHOTS_DIR</c> 指定，默认系统临时目录）
/// </summary>
[Collection(HeadlessCollection.Name)]
public class AudioModelTabsTests : IDisposable
{
    private const long GiB = 1L << 30;
    private readonly RecordingMessageService _messages = new();
    private readonly List<string> _openedFolders = [];
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
    public void Market_WithoutEngine_OffersToInstallIt()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = Open(new TestAudioEngine(installEngine: false));
            data.ShowAudioDownloads();
            await Settle(() => data.Downloads!.Audio is { IsLoading: false, ShowEnginePrompt: true });

            Assert.True(data.Downloads!.Audio.ShowEnginePrompt);
            Assert.Empty(data.Downloads!.Audio.Families);
            Assert.Single(_window!.GetVisualDescendants().OfType<AudioMarketListView>());
        });
    }

    [Fact]
    public void Market_ListsFamilies_FiltersAndSearches_AndShowsDetail()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = Open(new TestAudioEngine());
            data.ShowAudioDownloads();
            AudioMarketData market = data.Downloads!.Audio;
            await Settle(() => market.Families.Count == 3);

            // 推荐的在前，其次 supported，社区的最后
            Assert.Equal(["PocketTTS", "Qwen3-ASR", "Kokoro"], market.Families.Select(x => x.Title));
            Assert.True(market.Families[0].IsRecommended);
            Assert.NotNull(market.Families[2].MaturityText);

            market.SelectedFilter = market.FilterOptions.Single(x => x.Task == EAudioCppTask.Asr);
            Assert.Equal(["Qwen3-ASR"], market.Families.Select(x => x.Title));
            market.SelectedFilter = market.FilterOptions[0];
            market.SearchText = "zh"; //按语种搜
            Assert.Equal(["Qwen3-ASR", "Kokoro"], market.Families.Select(x => x.Title));
            market.SearchText = "";

            market.SelectedFamily = market.Families[0];
            await Settle(() => market.Detail is { IsLoading: false });
            AudioFamilyDetailData detail = market.Detail!;
            Assert.Equal(["English Q8_0", "German Q8_0"], detail.Rows.Select(x => x.Label));
            Assert.True(detail.Rows[0].IsRecommended);
            Assert.Equal("127.8 MB", detail.Rows[0].SizeText); //大小来自仓库列表
            Assert.Equal("Ready", detail.Rows[0].RiskTag);
            Assert.Contains(detail.Tags, x => x.Length > 0);
            Assert.NotNull(detail.VoicesText);
            Assert.False(detail.NeedsEspeak);
            Capture("audio-market");

            market.SelectedFamily = market.Families.Single(x => x.Title == "Kokoro");
            await Settle(() => market.Detail is { IsLoading: false } d && d.Family.Family == "kokoro_tts");
            Assert.True(market.Detail!.NeedsEspeak);
            Capture("audio-market-espeak");

            _window!.Width = 560; //窄窗：详情盖在列表上，有返回钮
            await Settle(() => true);
            Capture("audio-market-narrow");
        });
    }

    [Fact]
    public void Market_DownloadQueuesPackageFiles_AndInstalledRowCanBecomeDefault()
    {
        HeadlessUi.RunAsync(async () =>
        {
            TestAudioEngine audio = new();
            ModelPageData data = Open(audio);
            data.ShowAudioDownloads();
            AudioMarketData market = data.Downloads!.Audio;
            await Settle(() => market.Families.Count == 3);
            market.SelectedFamily = market.Families[0];
            await Settle(() => market.Detail is { IsLoading: false });
            AudioFamilyDetailData detail = market.Detail!;

            await detail.DownloadCommand.ExecuteAsync(detail.Rows[1]);
            DownloadJob job = Assert.Single(audio.Queue.Jobs);
            Assert.EndsWith(Path.Combine("audio.cpp", "PocketTTS-GGUF", "german", "pocket-tts-german-q8_0.gguf"),
                job.Request.DestinationPath);
            audio.Queue.Cancel(job);

            // 装好的那行可以设为默认，设完写进语音引擎设置
            audio.Install("PocketTTS-GGUF/english", "pocket_tts_english_q8_0", "pocket-tts-english-q8_0.gguf");
            detail.RefreshStatuses();
            Assert.True(detail.Rows[0].CanSetDefault);
            detail.SetDefaultCommand.Execute(detail.Rows[0]);
            Assert.Equal("pocket-tts-english-q8_0", audio.Settings.TtsModel);
        });
    }

    [Fact]
    public void InstalledTab_GroupsByTask_SetsDefaultAndVoice_AndDeletes()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<AudioCppModelEntry> models = [];
            TestAudioEngine audio = new(scanModels: () => models);
            string english = audio.Install("PocketTTS-GGUF/english", "pocket_tts_english_q8_0", "pocket-tts-english-q8_0.gguf");
            string german = audio.Install("PocketTTS-GGUF/german", "pocket_tts_german_q8_0", "pocket-tts-german-q8_0.gguf");
            string asr = audio.Install("Qwen3-ASR-0.6B-GGUF", "qwen3_asr_0_6b_q8_0", "qwen3-asr-0.6b-q8_0.gguf");
            models.AddRange(
            [
                new AudioCppModelEntry("pocket-tts-english-q8_0", "pocket_tts", english, "tts", ModelFile: english),
                new AudioCppModelEntry("pocket-tts-german-q8_0", "pocket_tts", german, "tts", ModelFile: german),
                new AudioCppModelEntry("qwen3-asr-0.6b-q8_0", "qwen3_asr", asr, "asr", ModelFile: asr)
            ]);
            ModelPageData data = Open(audio);
            data.ShowAudioModels();
            AudioModelListViewData list = data.AudioModels!;
            await Settle(() => list.IsLoaded);

            Assert.Equal(2, list.TtsModels.Count);
            AudioModelItemData first = list.TtsModels[0];
            AudioModelItemData second = list.TtsModels[1];
            Assert.Equal("PocketTTS", first.FamilyName); //家族名取自 spec
            Assert.True(first.IsDefault); //没选过时引擎会用第一个
            Assert.Contains(first.Voices, x => x.Id == "alba"); //预置音色不必启动引擎
            Assert.True(Assert.Single(list.AsrModels).IsDefault);
            Capture("audio-models");

            list.SetDefaultCommand.Execute(second);
            Assert.Equal("pocket-tts-german-q8_0", audio.Settings.TtsModel);
            Assert.False(first.IsDefault);
            Assert.True(second.IsDefault);

            second.SelectedVoice = second.Voices.Single(x => x.Id == "alba");
            Assert.Equal("alba", audio.Settings.TtsVoices["pocket-tts-german-q8_0"]);

            models.RemoveAt(1);
            await list.DeleteCommand.ExecuteAsync(second);
            Assert.False(File.Exists(german)); //按包标记连同包一起删
            Assert.Single(list.TtsModels);
        });
    }

    [Fact]
    public void InstalledTab_Preview_SynthesizesWithThatModelAndVoice_AndExplainsFailures()
    {
        HeadlessUi.RunAsync(async () =>
        {
            TestAudioEngine audio = new(scanModels: () =>
                [new AudioCppModelEntry("pocket-tts-english-q8_0", "pocket_tts", "/m", "tts", ModelFile: "/m/p.gguf")]);
            ModelPageData data = Open(audio);
            data.ShowAudioModels();
            await Settle(() => data.AudioModels!.IsLoaded);
            AudioModelItemData item = Assert.Single(data.AudioModels!.TtsModels);
            item.SelectedVoice = item.Voices.Single(x => x.Id == "alba");

            await data.AudioModels.PreviewCommand.ExecuteAsync(item);

            AudioSynthesisRequest request = Assert.Single(_synthesizer.Requests);
            Assert.Equal("pocket-tts-english-q8_0", request.Model); //试听的是这一行，不是默认模型
            Assert.Equal("alba", request.Voice);
            Assert.StartsWith("Hello", request.Text); //PocketTTS 只会英语之类，不念界面语言
            Assert.Equal(EAudioFormat.Wav, Assert.Single(_player.Played).Format);
            Assert.False(item.IsPreviewing);

            // 内存保护拒绝加载：翻成人话，不甩英文原文的 503
            _synthesizer.Error = AudioServiceException.FromResponse(System.Net.HttpStatusCode.ServiceUnavailable,
                """{"error":{"message":"estimated 0.30 GiB exceeds available","type":"insufficient_memory"}}""");
            await data.AudioModels.PreviewCommand.ExecuteAsync(item);
            (string message, _) = _messages.Notifications.Last();
            Assert.Contains("estimated 0.30 GiB", message);
            Assert.DoesNotContain("503", message);
        });
    }

    [Fact]
    public void InstalledTab_Empty_LeadsToTheVoiceMarket()
    {
        HeadlessUi.RunAsync(async () =>
        {
            ModelPageData data = Open(new TestAudioEngine());
            data.ShowAudioModels();
            await Settle(() => data.AudioModels!.IsLoaded);
            Assert.True(data.AudioModels!.ShowEmpty);

            data.AudioModels.GoToMarketCommand.Execute(null);
            await Settle(() => data.Downloads?.Audio.Families.Count == 3);
            Assert.Equal(ModelPageData.DownloadsTabIndex, data.SelectedTabIndex);
            Assert.Same(data.Downloads!.Audio, data.Downloads.SelectedMarket);
        });
    }

    [Fact]
    public void InstalledTab_CloneModels_ShowVoiceLibrary_InTheirVoicePicker()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<AudioCppModelEntry> models = [];
            TestAudioEngine audio = new(scanModels: () => models);
            string english = audio.Install("PocketTTS-GGUF/english", "pocket_tts_english_q8_0", "pocket-tts-english-q8_0.gguf");
            models.Add(new AudioCppModelEntry("pocket-tts-english-q8_0", "pocket_tts", english, "tts", ModelFile: english));
            VoiceLibrary library = new(Path.Combine(audio.ModelRoot, "..", "voices"));
            library.Save("小雨", WavEncoder.Encode(new float[24000 * 6], 24000, 1), VoiceReferenceText.Chinese[0]);
            VoiceLibraryViewData voices = new(library, audio.Settings, _messages, _player,
                () => Task.FromResult<VoiceSample?>(null), _openedFolders.Add, action => action());
            ModelPageData data = Open(audio, voices);
            data.ShowAudioModels();
            AudioModelListViewData list = data.AudioModels!;
            await Settle(() => list.IsLoaded);

            Assert.True(list.ShowVoices); //PocketTTS 会克隆
            Assert.NotEmpty(Assert.Single(voices.Items).DurationText);
            Assert.Equal(["", "alba", "小雨"], list.TtsModels[0].Voices.Select(x => x.Id));
            Capture("audio-voices");
        });
    }

    [Fact]
    public void InstalledTab_OnlineServices_ShareGroups_AndDefaultIsEitherLocalOrOnline()
    {
        HeadlessUi.RunAsync(async () =>
        {
            List<AudioCppModelEntry> models = [];
            TestAudioEngine audio = new(scanModels: () => models);
            string english = audio.Install("PocketTTS-GGUF/english", "pocket_tts_english_q8_0", "pocket-tts-english-q8_0.gguf");
            models.Add(new AudioCppModelEntry("pocket-tts-english-q8_0", "pocket_tts", english, "tts", ModelFile: english));
            AudioServiceSettingConfig config = new();
            config.ReplaceServices(
            [
                new AudioServiceInfo { Name = "Edge TTS", Dialect = EAudioDialect.EdgeTts },
                new AudioServiceInfo
                {
                    Name = "硅基流动 · 听写", Task = EAudioCapability.Asr, Endpoint = "https://api.siliconflow.cn/v1",
                    ModelId = "FunAudioLLM/SenseVoiceSmall"
                }
            ]);
            using AudioRouter router = new(() => _synthesizer, new AudioSettingConfig(), config, _ => _synthesizer,
                (task, _) => task == EAudioCapability.Tts);
            AudioServiceListViewData online = new(router, config, new ReadAloudService(_ => _synthesizer, _player, _messages),
                _messages, (_, _) => Task.FromResult<AudioServiceInfo?>(null));
            ModelPageData data = Open(audio, online: online);
            data.ShowAudioModels();
            AudioModelListViewData list = data.AudioModels!;
            await Settle(() => list.IsLoaded);

            AudioModelItemData pocket = Assert.Single(list.TtsModels);
            AudioServiceItemData edge = Assert.Single(online.TtsServices);
            Assert.True(pocket.IsDefault);
            Assert.False(edge.IsDefault);
            Assert.True(list.HasAsr); //本机没有识别模型，在线的也撑起听写分组
            Assert.True(Assert.Single(online.AsrServices).IsDefault); //本地没模型：退到在线的
            Capture("audio-online");

            online.SetDefaultCommand.Execute(edge);
            Assert.False(pocket.IsDefault);
            Assert.True(online.TtsServices[0].IsDefault);

            list.SetDefaultCommand.Execute(pocket);
            Assert.True(pocket.IsDefault);
            Assert.False(online.TtsServices[0].IsDefault);
        });
    }

    [Fact]
    public void AudioServiceEditWindow_Renders()
    {
        HeadlessUi.RunAsync(async () =>
        {
            AudioServiceEditViewData form = new(null, [], [], _ => Task.FromResult<IReadOnlyList<string>>([]),
                (_, _) => Task.CompletedTask, _messages);
            form.SelectedPreset = form.Presets.Single(x => x.Preset.Key == "siliconflow-tts");
            _window = new AudioServiceEditWindow { DataContext = form };
            _window.Show();
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(20);

            Assert.True(form.CanConfirm);
            Capture("audio-service-edit");
        });
    }

    [Fact]
    public void VoiceCreateWindow_RendersDraws()
    {
        HeadlessUi.RunAsync(async () =>
        {
            string root = Path.Combine(Path.GetTempPath(), $"uiharu-voice-window-{Guid.NewGuid():N}");
            VoiceCreateViewData form = new(new VoiceCreateContext(new VoiceLibrary(root), _player, new FakeAudioRecorder(),
                _messages, [new VoiceDrawModel("vox", "VoxCPM")],
                (_, _, _) => Task.FromResult(new VoiceDraft(WavEncoder.Encode(new float[24000 * 7], 24000, 1),
                    VoiceReferenceText.Chinese[0])),
                null, _ => throw new NotSupportedException(), (_, _) => { }, action => action()));
            await form.DrawCommand.ExecuteAsync(null);
            await form.DrawCommand.ExecuteAsync(null);
            _window = new VoiceCreateWindow { DataContext = form };
            _window.Show();
            Dispatcher.UIThread.RunJobs();

            Assert.True(form.CanSave);
            Capture("voice-create");
        });
    }

    private ModelPageData Open(TestAudioEngine audio, VoiceLibraryViewData? voices = null,
        AudioServiceListViewData? online = null)
    {
        _audio = audio;
        DownloadQueue queue = audio.Queue;
        ModelPageData data = new(_messages, page => new ModelDownloadPageData(
            new ModelDownloadContext(_messages, page.EnsureDownloadQueue(), () => Task.CompletedTask, () => { })
            {
                CreateSource = () => audio.Source,
                Queue = queue,
                Downloader = new ModelRepoDownloader(queue),
                ModelRoot = () => audio.ModelRoot,
                LocalModels = () => new Dictionary<string, string>(),
                EnsureEngine = () => Task.FromResult((EEngineEnsureState.AlreadyInstalled, (VersionInfo?)null, (DownloadJob?)null)),
                DeviceInfo = () => new RuntimeDeviceInfo(16 * GiB, 8 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now),
                AudioEngine = audio.Engine,
                AudioStore = audio.Store
            }), queue, createAudioModels: page => new AudioModelListViewData(_messages, audio.Engine, audio.Store,
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            new ReadAloudService(_ => _synthesizer, _player, _messages), _openedFolders.Add,
            page.ShowAudioDownloads, () => { }, voices, online, new AudioPreferencesViewData(new AudioSettingConfig { ReadAloudSpeed = 1.3f })));
        _window = new Window { Width = 1000, Height = 680, Content = new ModelPage { DataContext = data } };
        _window.Show();
        Dispatcher.UIThread.RunJobs();
        return data;
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
