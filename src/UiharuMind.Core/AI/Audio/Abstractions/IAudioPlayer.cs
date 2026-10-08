namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 出声（ADR 0071 的播放一级）：吃「字节流 + 格式」，不管是谁合成的。同一时刻只放一段，新的打断旧的
/// </summary>
public interface IAudioPlayer
{
    /// <summary>
    /// 正在放
    /// </summary>
    bool IsPlaying { get; }

    /// <summary>
    /// 放一段，放完（或被打断）才返回。流读完即可，释放仍归调用方
    /// </summary>
    /// <param name="audio">合成结果</param>
    /// <param name="cancellationToken">取消：停止播放并抛 <see cref="OperationCanceledException"/></param>
    /// <returns>放到了结尾为 true；被 <see cref="Stop"/> 或下一段打断为 false</returns>
    /// <exception cref="AudioDeviceException">没有可用的输出设备</exception>
    /// <exception cref="NotSupportedException">格式放不了（如 AIFF）</exception>
    Task<bool> PlayAsync(AudioSynthesisResult audio, CancellationToken cancellationToken);

    /// <summary>
    /// 打断正在放的（没在放就什么也不做）
    /// </summary>
    void Stop();
}

/// <summary>
/// 音频设备用不了：没有输出 / 输入设备、原生库加载失败等
/// </summary>
public sealed class AudioDeviceException(string message, Exception? inner = null) : Exception(message, inner);
