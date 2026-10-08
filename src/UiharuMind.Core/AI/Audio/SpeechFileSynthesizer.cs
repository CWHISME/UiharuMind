namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一段配好的语音：存在哪、多长
/// </summary>
/// <param name="Path">文件</param>
/// <param name="Duration">时长</param>
public sealed record SpeechFile(string Path, TimeSpan Duration);

/// <summary>
/// 把一段文字按某个声音合成成一个音频文件（智能体配音用）。逐句合成、统一成 24 kHz 单声道、
/// 模型自己变不了的语速在这里补（变速不变调），句间留一小段停顿，存成 WAV
/// </summary>
public sealed class SpeechFileSynthesizer
{
    /// <summary>
    /// 输出的采样率
    /// </summary>
    public const int SampleRate = 24000;

    private static readonly int GapSamples = SampleRate * 15 / 100; //句间停顿 0.15 秒

    private readonly Func<string?, IAudioProvider> _resolve;

    /// <param name="resolve">provider id（null 为默认）→ provider</param>
    public SpeechFileSynthesizer(Func<string?, IAudioProvider> resolve)
    {
        _resolve = resolve;
    }

    /// <summary>
    /// 走应用档案的那一个
    /// </summary>
    public static SpeechFileSynthesizer Shared { get; } =
        new(provider => AudioRouter.Shared.Resolve(EAudioCapability.Tts, provider));

    /// <summary>
    /// 合成并存盘
    /// </summary>
    /// <param name="text">文字（可以是 Markdown，记号不念）</param>
    /// <param name="style">声音；null 用默认</param>
    /// <param name="path">存到哪（.wav）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>存好的文件</returns>
    /// <exception cref="ArgumentException">没有可念的字</exception>
    public async Task<SpeechFile> SynthesizeAsync(string text, SpeechStyle? style, string path,
        CancellationToken cancellationToken)
    {
        string clean = SpeechText.Clean(text);
        IReadOnlyList<string> chunks = SpeechText.Split(clean);
        if (chunks.Count == 0) throw new ArgumentException("Nothing to speak.", nameof(text));

        IAudioProvider tts = _resolve(style?.Provider);
        AudioSynthesisRequest request = new("", style?.Voice, style?.Speed is { } speed and not 1f ? speed : null,
            style?.Model, SpeechText.DetectLanguage(clean), style?.Instruction);
        List<float> samples = [];
        foreach (string chunk in chunks)
        {
            AudioSynthesisResult result = await tts.SynthesizeAsync(request with { Text = chunk }, cancellationToken)
                .ConfigureAwait(false);
            float[] pcm;
            await using (result.Audio)
            {
                pcm = SoundFlowAudioFile.Stretch(SoundFlowAudioFile.Decode(result, SampleRate), result.PlaybackSpeed);
            }

            if (samples.Count > 0) samples.AddRange(new float[GapSamples]);
            samples.AddRange(pcm);
        }

        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, WavEncoder.Encode(samples.ToArray(), SampleRate, 1), cancellationToken)
            .ConfigureAwait(false);
        return new SpeechFile(path, TimeSpan.FromSeconds(samples.Count / (double)SampleRate));
    }
}
