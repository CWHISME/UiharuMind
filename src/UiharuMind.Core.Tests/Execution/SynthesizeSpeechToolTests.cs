using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 配音工具：按角色名取声音（不点用自己的）、语气与语速可这一次单独给；逐句合成拼成一个 WAV 存进草稿目录；
/// 模型变不了的语速在文件里补；点了不存在的角色列出有声音的角色
/// </summary>
public sealed class SynthesizeSpeechToolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-speech-" + Guid.NewGuid().ToString("N"));
    private readonly List<(string? Provider, AudioSynthesisRequest Request)> _requests = [];
    private readonly CharacterData _self = new() { CharacterId = "self", CharacterName = "Me" };
    private readonly CharacterData _alice = new()
    {
        CharacterId = "alice", CharacterName = "Alice",
        Speech = new SpeechStyle { Provider = AudioCppProvider.LocalId, Model = "vox", Voice = "a", Instruction = "warm" }
    };

    private float _modelSpeed = 1f; //假模型变不了速时交回的 PlaybackSpeed

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task NamedCharacter_VoiceAndOneOffTone_AllSentencesInOneFile()
    {
        string text = string.Concat(Enumerable.Repeat("This sentence is long enough to be spoken on its own line. ", 6));
        string result = await Invoke(new AIFunctionArguments { ["text"] = text, ["voice"] = "alice", ["tone"] = "angry" });

        Assert.StartsWith("Saved: ", result);
        Assert.Contains("Alice", result);
        Assert.Equal("$DRAFT/audio/", SynthesizeSpeechTool.ParseSavedPaths(result).Single()[..13]); //卡片据此找文件
        Assert.All(_requests, x =>
        {
            Assert.Equal(AudioCppProvider.LocalId, x.Provider);
            Assert.Equal(("vox", "a", "angry"), (x.Request.Model, x.Request.Voice, x.Request.Instruction));
        });
        string file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "draft", SynthesizeSpeechTool.AudioFolder)));
        TimeSpan duration = WavEncoder.GetDuration(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken))!.Value;
        int sentences = _requests.Count;
        Assert.True(sentences >= 2);
        Assert.Equal(sentences + (sentences - 1) * 0.15, duration.TotalSeconds, 0.05); //每段一秒，段间停 0.15 秒
    }

    [Fact]
    public async Task OwnVoiceByDefault_AndSpeedTheModelCannotDoIsAppliedToTheFile()
    {
        _modelSpeed = 2f;
        string result = await Invoke(new AIFunctionArguments { ["text"] = "Hello.", ["speed"] = 2f });

        Assert.Contains("Me", result);
        (string? provider, AudioSynthesisRequest request) = Assert.Single(_requests);
        Assert.Null(provider);
        Assert.Equal(2f, request.Speed);
        string file = Assert.Single(Directory.GetFiles(Path.Combine(_root, "draft", SynthesizeSpeechTool.AudioFolder)));
        TimeSpan duration = WavEncoder.GetDuration(await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken))!.Value;
        Assert.Equal(0.5, duration.TotalSeconds, 0.08); //一秒的音频按两倍速补成半秒
    }

    [Fact]
    public async Task UnknownCharacter_ListsThoseWithAVoice()
    {
        string result = await Invoke(new AIFunctionArguments { ["text"] = "Hi.", ["voice"] = "Bob" });

        Assert.StartsWith("Error: 没有名为 'Bob' 的角色。", result);
        Assert.Contains("Alice", result);
        Assert.DoesNotContain("Me", result); //没设声音的不列
        Assert.Empty(_requests);
    }

    private async Task<string> Invoke(AIFunctionArguments arguments)
    {
        SpeechFileSynthesizer synthesizer = new(provider => new SineProvider(provider, this));
        AIFunction tool = (AIFunction)SynthesizeSpeechTool.Create(
            new AgentPathResolver(Path.Combine(_root, "workspace"), Path.Combine(_root, "draft")), _self, synthesizer,
            () => [_self, _alice]);
        return (await tool.InvokeAsync(arguments, TestContext.Current.CancellationToken))?.ToString() ?? "";
    }

    // 每句回一秒 16 kHz 正弦，记下请求
    private sealed class SineProvider(string? id, SynthesizeSpeechToolTests owner) : IAudioProvider
    {
        public string Id => id ?? "default";
        public string DisplayName => Id;
        public EAudioProviderKind Kind => EAudioProviderKind.LocalEngine;
        public IReadOnlySet<EAudioCapability> Capabilities { get; } = new HashSet<EAudioCapability> { EAudioCapability.Tts };
        public bool IsLocal => true;

        public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken)
        {
            owner._requests.Add((id, request));
            float[] tone = new float[16000];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.3f * MathF.Sin(2 * MathF.PI * 220 * i / 16000);
            return Task.FromResult(new AudioSynthesisResult(new MemoryStream(WavEncoder.Encode(tone, 16000, 1)),
                EAudioFormat.Wav, PlaybackSpeed: owner._modelSpeed));
        }

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
            CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
