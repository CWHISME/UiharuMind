namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一段识别结果：起止时间与文字
/// </summary>
/// <param name="Start">起</param>
/// <param name="End">止</param>
/// <param name="Text">文字</param>
public sealed record TranscriptSegment(TimeSpan Start, TimeSpan End, string Text);

/// <summary>
/// 一个音频文件的识别结果
/// </summary>
/// <param name="Segments">各段（无声、没认出字的段不在里面）</param>
/// <param name="Duration">识别了多长</param>
/// <param name="Truncated">太长，只识别了前 <see cref="AudioFileTranscriber.MaxDuration"/></param>
public sealed record Transcript(IReadOnlyList<TranscriptSegment> Segments, TimeSpan Duration, bool Truncated)
{
    /// <summary>
    /// 全文（段间按语种接：两边有一边是中日韩文字就直接接，否则补个空格）
    /// </summary>
    public string Text => Segments.Aggregate("", (text, segment) =>
        text.Length == 0 ? segment.Text
        : IsCjk(text[^1]) || IsCjk(segment.Text[0]) ? text + segment.Text
        : text + " " + segment.Text);

    private static bool IsCjk(char c) => c >= '\u2E80';
}

/// <summary>
/// 把音频文件转成文字（智能体用）：解成 16 kHz 单声道，按停顿切段逐段识别——段的起止就是字幕的时间轴，
/// 与用哪家识别无关。字幕模式切得短（一段一两句），纯文字模式切得长（少发几次请求）
/// </summary>
public sealed class AudioFileTranscriber
{
    /// <summary>
    /// 最多识别多长（再长只取开头，内存与等待都有数）
    /// </summary>
    public static readonly TimeSpan MaxDuration = TimeSpan.FromMinutes(30);

    private const int SampleRate = IAudioRecorder.SpeechSampleRate;

    private readonly Func<IAudioProvider> _asr;
    private readonly Func<string, int, TimeSpan, (float[] Samples, bool Truncated)> _decode;

    /// <param name="asr">识别用的 provider（每次现取）</param>
    /// <param name="decode">解码：路径、采样率、最长 → 单声道采样、是否截断；null 用 SoundFlow</param>
    public AudioFileTranscriber(Func<IAudioProvider> asr,
        Func<string, int, TimeSpan, (float[] Samples, bool Truncated)>? decode = null)
    {
        _asr = asr;
        _decode = decode ?? SoundFlowAudioFile.DecodeFile;
    }

    /// <summary>
    /// 走应用档案的那一个（听写默认用哪个就用哪个）
    /// </summary>
    public static AudioFileTranscriber Shared { get; } = new(() => AudioRouter.Shared.Resolve(EAudioCapability.Asr));

    /// <summary>
    /// 识别
    /// </summary>
    /// <param name="path">音频文件（wav / mp3 / flac）</param>
    /// <param name="subtitles">切成字幕长短的段</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>结果</returns>
    /// <exception cref="NotSupportedException">格式解不了</exception>
    public async Task<Transcript> TranscribeAsync(string path, bool subtitles, CancellationToken cancellationToken)
    {
        (float[] samples, bool truncated) = await Task.Run(() => _decode(path, SampleRate, MaxDuration), cancellationToken)
            .ConfigureAwait(false);
        IReadOnlyList<AudioSpan> spans = subtitles
            ? SpeechSegmenter.Split(samples, SampleRate, TimeSpan.FromSeconds(1.5), TimeSpan.FromSeconds(8))
            : SpeechSegmenter.Split(samples, SampleRate, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30));

        IAudioProvider asr = _asr();
        List<TranscriptSegment> segments = [];
        foreach (AudioSpan span in spans)
        {
            using MemoryStream wav = new(WavEncoder.Encode(samples.AsSpan(span.Start, span.Length), SampleRate, 1), false);
            AudioTranscriptionResult result = await asr.TranscribeAsync(new AudioTranscriptionRequest(wav, EAudioFormat.Wav),
                cancellationToken).ConfigureAwait(false);
            string text = result.Text.Trim();
            if (text.Length > 0) segments.Add(new TranscriptSegment(At(span.Start), At(span.End), text));
        }

        return new Transcript(segments, At(samples.Length), truncated);
    }

    private static TimeSpan At(int sample) => TimeSpan.FromSeconds(sample / (double)SampleRate);
}
