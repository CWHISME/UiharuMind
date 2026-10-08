namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 录音（ADR 0071 的录音一级）：从默认麦克风录成语音识别要的 16 kHz 单声道
/// </summary>
public interface IAudioRecorder
{
    /// <summary>
    /// 正在录
    /// </summary>
    bool IsRecording { get; }

    /// <summary>
    /// 当前音量（0..1 的峰值），在音频线程上触发，界面要自己切线程
    /// </summary>
    event Action<float>? LevelChanged;

    /// <summary>
    /// 开始录
    /// </summary>
    /// <exception cref="AudioDeviceException">没有可用的麦克风</exception>
    /// <exception cref="InvalidOperationException">已经在录</exception>
    void Start();

    /// <summary>
    /// 停止并取回录到的
    /// </summary>
    /// <returns>录音；没在录为 null</returns>
    AudioRecording? Stop();

    /// <summary>
    /// 停止并丢掉
    /// </summary>
    void Cancel();
}

/// <summary>
/// 一段录音
/// </summary>
/// <param name="Wav">16 kHz 单声道 PCM16 的 WAV</param>
/// <param name="Duration">时长</param>
/// <param name="Peak">最大振幅（0..1）。几乎为 0 多半是没声音，或系统没给麦克风权限（macOS 拒绝时只会录到静音）</param>
public sealed record AudioRecording(byte[] Wav, TimeSpan Duration, float Peak)
{
    /// <summary>
    /// 静音阈值：峰值低于它当作什么也没录到
    /// </summary>
    public const float SilenceThreshold = 0.003f;

    /// <summary>
    /// 什么也没录到
    /// </summary>
    public bool IsSilent => Peak < SilenceThreshold;

    /// <summary>
    /// 交给听写的请求（流归调用方释放）
    /// </summary>
    /// <returns>请求</returns>
    public AudioTranscriptionRequest ToRequest() => new(new MemoryStream(Wav, false), EAudioFormat.Wav);
}
