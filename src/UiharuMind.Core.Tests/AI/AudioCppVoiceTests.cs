using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 音色库与合成时的音色选择：库的文件形状照 audio.cpp 的 voice_dir；会克隆却没自带音色的模型第一次用时生成固定音色；抽卡带种子
/// </summary>
public class AudioCppVoiceTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-audiocpp-voices-{Guid.NewGuid():N}");
    private readonly AudioCppSettingConfig _settings = new();
    private readonly VoiceLibrary _library;

    public AudioCppVoiceTests()
    {
        Directory.CreateDirectory(_directory);
        _library = new VoiceLibrary(Path.Combine(_directory, "voices"));
    }

    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }

    [Fact]
    public void Library_WritesWavAndPromptTextInVoiceDirShape()
    {
        int changes = 0;
        _library.Changed += () => changes++;

        _library.Save("小雨", [1, 2, 3], "第一行\n第二行");
        _library.Save("alba", [4], "hello");

        Assert.Equal([1, 2, 3], File.ReadAllBytes(Path.Combine(_library.Directory, "小雨.wav")));
        Assert.Equal(["alba|hello", "小雨|第一行 第二行"],
            File.ReadAllLines(Path.Combine(_library.Directory, VoiceLibrary.TranscriptFileName)));
        Assert.Equal(["alba", "小雨"], _library.List().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal("第一行 第二行", _library.List().Single(x => x.Name == "小雨").Transcript);

        _library.Delete("alba");
        Assert.Equal(["小雨"], _library.List().Select(x => x.Name));
        Assert.Equal(["小雨|第一行 第二行"], File.ReadAllLines(Path.Combine(_library.Directory, VoiceLibrary.TranscriptFileName)));
        Assert.Equal(3, changes);
    }

    [Theory]
    [InlineData("  我的 声音 ", "我的 声音")]
    [InlineData("a/b|c", "a_b_c")]
    [InlineData("..", null)]
    [InlineData("   ", null)]
    [InlineData("name.", "name")]
    public void NormalizeName_KeepsReadableNames_AndStripsPathCharacters(string input, string? expected)
    {
        Assert.Equal(expected, VoiceLibrary.NormalizeName(input));
    }

    [Fact]
    public void MakeUnique_AppendsNumber()
    {
        _library.Save("VoxCPM", [1], "x");
        _library.Save("VoxCPM 2", [1], "x");

        Assert.Equal("VoxCPM 3", _library.MakeUnique("VoxCPM"));
        Assert.Equal("Other", _library.MakeUnique("Other"));
    }

    [Fact]
    public void ReferenceText_DrawPool_FollowsModelLanguages()
    {
        Assert.All(Enumerable.Range(0, 20), seed =>
            Assert.Contains(VoiceReferenceText.ForDraw(["en", "de"], seed), VoiceReferenceText.English));
        HashSet<string> mixed = Enumerable.Range(0, 20).Select(seed => VoiceReferenceText.ForDraw(["zh", "en"], seed)).ToHashSet();
        Assert.Contains(mixed, VoiceReferenceText.Chinese.Contains);
        Assert.Contains(mixed, VoiceReferenceText.English.Contains);
        Assert.Equal(VoiceReferenceText.English[0], VoiceReferenceText.For(["zh", "en"], "en"));
        Assert.Equal(VoiceReferenceText.Chinese[0], VoiceReferenceText.For([]));
    }

    [Fact]
    public void WavDuration_ReadsHeader_AndToleratesStreamingLength()
    {
        byte[] wav = WavEncoder.Encode(new float[24000], 24000, 1);
        Assert.Equal(TimeSpan.FromSeconds(1), WavEncoder.GetDuration(wav));

        BitConverter.GetBytes(-1).CopyTo(wav, 40); //流式写出的 data 长度
        Assert.Equal(TimeSpan.FromSeconds(1), WavEncoder.GetDuration(wav));
        Assert.Null(WavEncoder.GetDuration([1, 2, 3]));
    }

    [Fact]
    public async Task CloneModelWithoutVoices_SeedsOneFixedVoice_AndKeepsUsingIt()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(new AudioCppModelEntry("vox", "voxcpm1", "/m/v.gguf", "tts"));
        using AudioCppProvider provider = new(engine);

        Assert.Equal("vox:VoxCPM", await Synthesize(provider, "第一句"));
        Assert.Equal("vox:VoxCPM", await Synthesize(provider, "Second sentence"));

        VoiceSample voice = Assert.Single(_library.List());
        Assert.Equal(VoiceReferenceText.Chinese[0], voice.Transcript);
        Assert.Equal("vox:auto:seed=None:" + VoiceReferenceText.Chinese[0], File.ReadAllText(voice.Path));
        Assert.Empty(_settings.TtsVoices); //界面上仍是「默认」
    }

    [Fact]
    public async Task NativeVoices_LeaveOutTheLibrary()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        _library.Save("mine", [1], "x");
        using AudioCppEngine engine = CreateEngine(new AudioCppModelEntry("pocket", "pocket_tts", "/m/p.gguf", "tts"));
        using AudioCppProvider provider = new(engine);

        Assert.Equal(["alba", "marius"], await provider.GetVoicesAsync("pocket", CancellationToken.None));
        Assert.Equal("pocket:alba", await Synthesize(provider, "hi"));
    }

    [Fact]
    public async Task DrawVoice_SendsTheSeed_AndReadsAPoolSentence()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(new AudioCppModelEntry("vox", "voxcpm1", "/m/v.gguf", "tts"));
        using AudioCppProvider provider = new(engine);

        VoiceDraft draft = await provider.DrawVoiceAsync("vox", 7, CancellationToken.None);

        Assert.Equal(VoiceReferenceText.ForDraw(["zh", "en"], 7), draft.Transcript);
        Assert.Equal("vox:auto:seed=7:" + draft.Transcript, System.Text.Encoding.UTF8.GetString(draft.Wav));
        Assert.Empty(_library.List()); //抽到的只是草稿，存不存由用户定
    }

    [Fact]
    public async Task CloneOnlyModel_UsesLibrary_OrAsksForAVoice()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fake server is a shell script");
        using AudioCppEngine engine = CreateEngine(new AudioCppModelEntry("echo", "echo_tts", "/m/e.gguf", "tts"));
        using AudioCppProvider provider = new(engine);

        await Assert.ThrowsAsync<AudioVoiceRequiredException>(() => Synthesize(provider, "hi"));

        _library.Save("mine", [1], "x");
        Assert.Equal("echo:mine", await Synthesize(provider, "hi"));
    }

    private static async Task<string> Synthesize(AudioCppProvider provider, string text)
    {
        AudioSynthesisResult result = await provider.SynthesizeAsync(new AudioSynthesisRequest(text), CancellationToken.None);
        using StreamReader reader = new(result.Audio);
        return await reader.ReadToEndAsync();
    }

    private AudioCppEngine CreateEngine(AudioCppModelEntry model)
    {
        string install = Path.Combine(_directory, "engine");
        string specs = Path.Combine(install, "model_specs");
        Directory.CreateDirectory(specs);
        File.WriteAllText(Path.Combine(specs, "voxcpm1.json"), Spec("voxcpm1", "VoxCPM", ["tts", "clone"], ["zh", "en"]));
        File.WriteAllText(Path.Combine(specs, "pocket_tts.json"), Spec("pocket_tts", "PocketTTS", ["tts", "clone"], ["en"]));
        File.WriteAllText(Path.Combine(specs, "echo_tts.json"), Spec("echo_tts", "Echo", ["clone"], ["en"]));
        string bin = Path.Combine(_directory, "bin");
        Directory.CreateDirectory(bin);
        FakeAudioCppServer.Write(Path.Combine(bin, AudioCppVersionManager.ServerExeName), _directory);

        AudioCppEngine engine = new(_settings, Path.Combine(_directory, "engines"), Path.Combine(_directory, "server.json"),
            () => [model], _library);
        engine.UseVersion(new AudioCppVersion
        {
            Name = "audio-v0.9.1-bin-macos-arm64-metal", ExecutablePath = bin, InstallDirectory = install
        });
        return engine;
    }

    private static string Spec(string family, string displayName, string[] tasks, string[] languages) =>
        "{\"family\":\"" + family + "\",\"display_name\":\"" + displayName + "\",\"category\":\"tts\"," +
        "\"tasks\":[" + string.Join(",", tasks.Select(x => "\"" + x + "\"")) + "]," +
        "\"languages\":[" + string.Join(",", languages.Select(x => "\"" + x + "\"")) + "]," +
        "\"package_defaults\":{\"download\":{\"kind\":\"huggingface_snapshot\",\"repo\":\"a/b\"}}," +
        "\"packages\":[{\"id\":\"" + family + "_q8\",\"format\":\"gguf\",\"target_directory\":\"D\",\"files\":[\"D/a.gguf\"]}]}";
}
