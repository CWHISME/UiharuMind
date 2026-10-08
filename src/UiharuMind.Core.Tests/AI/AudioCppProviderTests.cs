using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 本地引擎 provider 端到端：假 audiocpp_server（python 起一个迷你服务）走完设置 → server.json → 进程 → 请求。
/// 假服务和 pocket_tts 一样：合成不给音色就 500
/// </summary>
public class AudioCppProviderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-audiocpp-provider-{Guid.NewGuid():N}");
    private readonly AudioCppSettingConfig _settings = new();
    private readonly List<AudioCppModelEntry> _models =
    [
        new("tts-a", "pocket_tts", "/models/a.gguf", "tts"),
        new("asr-a", "moonshine_asr", "/models/m.gguf", "asr")
    ];

    public AudioCppProviderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public async Task Synthesize_WithoutVoice_PicksFirstListedVoice_ThenTranscribes()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);

        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("hello"), CancellationToken.None);
        Assert.Equal(EAudioFormat.Wav, tts.Format);
        Assert.Equal("tts-a:alba", await ReadText(tts.Audio));

        AudioTranscriptionResult asr = await provider.TranscribeAsync(
            new AudioTranscriptionRequest(new MemoryStream([1, 2, 3]), EAudioFormat.Wav), CancellationToken.None);
        Assert.Equal("asr-a heard you", asr.Text);
    }

    [Fact]
    public async Task Status_OnlyAsksARunningEngine_AndListsLoadedModels()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);

        Assert.Equal(EAudioEngineRunState.Stopped, (await engine.GetStatusAsync(CancellationToken.None)).State);
        Assert.False(File.Exists(Path.Combine(_directory, "starts"))); //查状态不会把引擎拉起来

        await (await provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), CancellationToken.None)).Audio.DisposeAsync();
        AudioCppEngineStatus status = await engine.GetStatusAsync(CancellationToken.None);
        Assert.Equal(EAudioEngineRunState.Running, status.State);
        Assert.Equal("metal", status.Backend);
        Assert.Equal(["tts-a"], status.LoadedModels);

        engine.Stop();
        Assert.Equal(EAudioEngineRunState.Stopped, (await engine.GetStatusAsync(CancellationToken.None)).State);
    }

    [Fact]
    public async Task Synthesize_UsesVoiceChosenForThatModel()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        _settings.TtsVoices["tts-a"] = "marius";
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);

        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("hello"), CancellationToken.None);

        Assert.Equal("tts-a:marius", await ReadText(tts.Audio));
    }

    [Fact]
    public async Task SelectingModelTheServerDoesNotKnow_RestartsWithIt()
    {
        // 刚下完的模型不在已起的 server.json 里，不重启就会被 server 拒掉
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(installed: true);
        using AudioCppProvider provider = new(engine);
        await provider.SynthesizeAsync(new AudioSynthesisRequest("one"), CancellationToken.None);

        _models.Add(new AudioCppModelEntry("tts-b", "kokoro_tts", "/models/b.gguf", "tts"));
        _settings.TtsModel = "tts-b";
        AudioSynthesisResult tts = await provider.SynthesizeAsync(new AudioSynthesisRequest("two"), CancellationToken.None);

        Assert.Equal("tts-b:alba", await ReadText(tts.Audio));
        Assert.Equal(2, File.ReadAllLines(Path.Combine(_directory, "starts")).Length);
    }

    [Fact]
    public async Task NoEngineInstalled_FailsAsNotReady()
    {
        using AudioCppEngine engine = CreateEngine(installed: false);
        using AudioCppProvider provider = new(engine);

        await Assert.ThrowsAsync<LocalEngineNotReadyException>(() =>
            provider.SynthesizeAsync(new AudioSynthesisRequest("hi"), CancellationToken.None));
    }

    [Fact]
    public async Task NoModelForTask_FailsAsModelMissing()
    {
        _models.RemoveAll(x => x.Task == "asr");
        using AudioCppEngine engine = CreateEngine(installed: false);
        using AudioCppProvider provider = new(engine);

        AudioModelMissingException error = await Assert.ThrowsAsync<AudioModelMissingException>(() =>
            provider.TranscribeAsync(new AudioTranscriptionRequest(new MemoryStream(), EAudioFormat.Wav),
                CancellationToken.None));
        Assert.Equal(EAudioCppTask.Asr, error.Task);
    }

    [Fact]
    public void InstalledPackage_BecomesSelected_OnlyWhenNothingChosenYet()
    {
        using AudioCppEngine engine = CreateEngine(installed: false);
        AudioCppPackage asr = Package("asr-a", EAudioCppTask.Asr);
        _settings.TtsModel = "tts-a";

        engine.OnPackageInstalled(asr);
        engine.OnPackageInstalled(Package("tts-b", EAudioCppTask.Tts));

        Assert.Equal("asr-a", _settings.AsrModel);
        Assert.Equal("tts-a", _settings.TtsModel); // 选过的不动；tts-b 也不在模型目录里
    }

    [Fact]
    public void ServerPath_IsPackageDirectory_OnlyWhenItHoldsOneGguf()
    {
        string single = Path.Combine(_directory, "single");
        string shared = Path.Combine(_directory, "shared");
        Directory.CreateDirectory(single);
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(single, "a.gguf"), "");
        File.WriteAllText(Path.Combine(shared, "a-q8_0.gguf"), "");
        File.WriteAllText(Path.Combine(shared, "a-bf16.gguf"), "");

        Assert.Equal(single, AudioCppModels.ServerPath(Path.Combine(single, "a.gguf")));
        Assert.Equal(Path.Combine(shared, "a-bf16.gguf"), AudioCppModels.ServerPath(Path.Combine(shared, "a-bf16.gguf")));
    }

    [Theory]
    [InlineData("pocket", "zh", "vox")] //选的不会中文，换会中文的
    [InlineData("pocket", "en", "pocket")]
    [InlineData("pocket", "ja", "pocket")] //谁都不会：还用选的
    [InlineData("vox", "en", "vox")]
    [InlineData("mystery", "zh", "mystery")] //spec 认不出的不当作「不会」
    [InlineData("pocket", null, "pocket")]
    public void ResolveModel_ByLanguage_PrefersChosenUnlessItCannotSpeakIt(string chosen, string? language, string expected)
    {
        string install = Path.Combine(_directory, "engine");
        Directory.CreateDirectory(Path.Combine(install, "model_specs"));
        File.WriteAllText(Path.Combine(install, "model_specs", "pocket_tts.json"), Spec("pocket_tts", "en", "de"));
        File.WriteAllText(Path.Combine(install, "model_specs", "voxcpm1.json"), Spec("voxcpm1", "zh", "en"));
        _models.Clear();
        _models.AddRange([
            new("pocket", "pocket_tts", "/m/p.gguf", "tts"), new("vox", "voxcpm1", "/m/v.gguf", "tts"),
            new("mystery", "unknown_family", "/m/x.gguf", "tts")
        ]);
        _settings.TtsModel = chosen;
        using AudioCppEngine engine = CreateEngine(installed: false);
        engine.UseVersion(new AudioCppVersion { Name = "audio-v0.9.1-bin-macos-arm64-metal", InstallDirectory = install });

        Assert.Equal(expected, engine.ResolveModel(EAudioCppTask.Tts, language).Id);
    }

    private static string Spec(string family, params string[] languages) =>
        "{\"family\":\"" + family + "\",\"category\":\"tts\",\"languages\":[" +
        string.Join(",", languages.Select(x => "\"" + x + "\"")) + "]," +
        "\"package_defaults\":{\"download\":{\"kind\":\"huggingface_snapshot\",\"repo\":\"a/b\"}}," +
        "\"packages\":[{\"id\":\"" + family + "_q8\",\"format\":\"gguf\",\"target_directory\":\"D\",\"files\":[\"D/a.gguf\"]}]}";

    private AudioCppEngine CreateEngine(bool installed)
    {
        AudioCppEngine engine = new(_settings, Path.Combine(_directory, "engines"),
            Path.Combine(_directory, "server.json"), () => _models.ToList());
        if (installed)
        {
            string bin = Path.Combine(_directory, "bin");
            Directory.CreateDirectory(bin);
            FakeAudioCppServer.Write(Path.Combine(bin, AudioCppVersionManager.ServerExeName), _directory);
            engine.UseVersion(new AudioCppVersion { Name = "audio-v0.9.1-bin-macos-arm64-metal", ExecutablePath = bin });
        }

        return engine;
    }

    private static AudioCppPackage Package(string modelName, EAudioCppTask task) => new(
        modelName, "f", task, modelName, "q8_0", true, "supported", [], "D", "", [$"D/{modelName}.gguf"],
        EAudioCppPackageHost.HuggingFace, "r/r", null, false);

    private static async Task<string> ReadText(Stream stream)
    {
        using StreamReader reader = new(stream);
        return await reader.ReadToEndAsync();
    }
}
