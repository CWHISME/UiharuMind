using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 转写：按停顿切段（不超上限、在安静处下刀、无声段不送）；逐段识别拼全文、存 txt，字幕模式另存 SRT；
/// 格式不对、文件不在给出能照做的提示
/// </summary>
public sealed class TranscribeAudioToolTests : IDisposable
{
    private const int Rate = 16000;
    private readonly string _root = Path.Combine(Path.GetTempPath(), "uiharu-transcribe-" + Guid.NewGuid().ToString("N"));
    private readonly List<int> _sentLengths = []; //每次送去识别的采样数

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Segmenter_CutsInPauses_DropsSilence_AndRespectsTheMaximum()
    {
        // 说 3 秒、停 1 秒、说 3 秒、静 5 秒、说 12 秒（中间没停顿）
        float[] audio = [.. Speech(3), .. Silence(1), .. Speech(3), .. Silence(5), .. Speech(12)];

        IReadOnlyList<AudioSpan> spans = SpeechSegmenter.Split(audio, Rate, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8));

        Assert.All(spans, x => Assert.True(x.Length <= 8 * Rate));
        Assert.InRange(spans[0].End, 3 * Rate, 4 * Rate); //第一刀落在第一次停顿里
        Assert.DoesNotContain(spans, x => x.Start >= 7 * Rate + Rate && x.End <= 12 * Rate); //整段无声的没送
        Assert.Equal(audio.Length, spans[^1].End);
    }

    [Fact]
    public async Task Subtitles_WriteTextAndSrt_AndReturnAnExcerpt()
    {
        string result = await Invoke("talk.wav", subtitles: true,
            [.. Speech(3), .. Silence(1), .. Speech(3), .. Silence(1), .. Speech(3)]);

        Assert.StartsWith("Transcribed 11 s", result);
        Assert.Contains("Subtitles: $DRAFT/transcripts/talk.srt", result);
        string srt = await File.ReadAllTextAsync(Path.Combine(_root, "draft", "transcripts", "talk.srt"),
            TestContext.Current.CancellationToken);
        Assert.StartsWith("1\n00:00:00,000 --> 00:00:0", srt);
        Assert.Contains("\n2\n", srt);
        string text = await File.ReadAllTextAsync(Path.Combine(_root, "draft", "transcripts", "talk.txt"),
            TestContext.Current.CancellationToken);
        Assert.Equal("第1段第2段第3段", text); //中文段间不补空格
        Assert.EndsWith(text, result);
    }

    [Fact]
    public async Task PlainMode_SendsFewerLongerPieces()
    {
        await Invoke("talk.wav", subtitles: false, [.. Speech(3), .. Silence(1), .. Speech(3), .. Silence(1), .. Speech(3)]);

        Assert.Single(_sentLengths); //11 秒不到 30 秒上限，一次送完
        Assert.False(File.Exists(Path.Combine(_root, "draft", "transcripts", "talk.srt")));
    }

    [Fact]
    public async Task WrongFormatOrMissingFile_SaysWhatToDo()
    {
        Assert.Contains("ffmpeg", await Invoke("clip.mp4", subtitles: false, Speech(1)));
        Assert.StartsWith("Error: file not found", await Invoke("nope.wav", subtitles: false, Speech(1), create: false));
        Assert.Empty(_sentLengths);
    }

    private async Task<string> Invoke(string file, bool subtitles, float[] audio, bool create = true)
    {
        string workspace = Path.Combine(_root, "workspace");
        Directory.CreateDirectory(workspace);
        if (create) await File.WriteAllBytesAsync(Path.Combine(workspace, file), [0], TestContext.Current.CancellationToken);
        AudioFileTranscriber transcriber = new(() => new CountingAsr(_sentLengths), (_, _, _) => (audio, false));
        AIFunction tool = (AIFunction)TranscribeAudioTool.Create(new AgentPathResolver(workspace, Path.Combine(_root, "draft")),
            transcriber);
        return (await tool.InvokeAsync(new AIFunctionArguments { ["path"] = file, ["subtitles"] = subtitles },
            TestContext.Current.CancellationToken))?.ToString() ?? "";
    }

    private static float[] Speech(double seconds) =>
        Enumerable.Range(0, (int)(seconds * Rate)).Select(i => 0.3f * MathF.Sin(2 * MathF.PI * 200 * i / Rate)).ToArray();

    private static float[] Silence(double seconds) => new float[(int)(seconds * Rate)];

    // 第几次识别就回「第 N 段」
    private sealed class CountingAsr(List<int> sent) : IAudioProvider
    {
        public string Id => "asr";
        public string DisplayName => "asr";
        public EAudioProviderKind Kind => EAudioProviderKind.LocalEngine;
        public IReadOnlySet<EAudioCapability> Capabilities { get; } = new HashSet<EAudioCapability> { EAudioCapability.Asr };
        public bool IsLocal => true;

        public Task<AudioSynthesisResult> SynthesizeAsync(AudioSynthesisRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AudioTranscriptionResult> TranscribeAsync(AudioTranscriptionRequest request,
            CancellationToken cancellationToken)
        {
            sent.Add((int)((request.Audio.Length - 44) / 2));
            return Task.FromResult(new AudioTranscriptionResult($"第{sent.Count}段"));
        }
    }
}
