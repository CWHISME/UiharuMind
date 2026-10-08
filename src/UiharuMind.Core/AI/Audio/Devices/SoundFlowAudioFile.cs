using SoundFlow.Interfaces;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 用 SoundFlow 读音频文件（wav / mp3 / flac），转成指定采样率的单声道 WAV
/// </summary>
public static class SoundFlowAudioFile
{
    /// <summary>
    /// 能读的扩展名
    /// </summary>
    public static readonly IReadOnlyList<string> Extensions = ["wav", "mp3", "flac"];

    /// <summary>
    /// 读一个音频文件，超出 <paramref name="maxDuration"/> 的截掉
    /// </summary>
    /// <param name="path">文件</param>
    /// <param name="sampleRate">转成的采样率</param>
    /// <param name="maxDuration">最长取多久</param>
    /// <returns>转好的音频</returns>
    /// <exception cref="NotSupportedException">格式不认识或解不开</exception>
    public static AudioRecording Read(string path, int sampleRate, TimeSpan maxDuration)
    {
        string format = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
        if (!Extensions.Contains(format)) throw new NotSupportedException($"Unsupported audio format '{format}'.");

        using FileStream stream = File.OpenRead(path);
        ISoundDecoder decoder;
        try
        {
            decoder = SoundFlowRuntime.Engine.CreateDecoder(stream, format, SoundFlowRuntime.FloatFormat(sampleRate, 1));
        }
        catch (Exception e) when (e is not NotSupportedException and not AudioDeviceException)
        {
            throw new NotSupportedException($"Cannot decode '{path}': {e.Message}", e);
        }

        using (decoder)
        {
            float[] samples = new float[(int)(sampleRate * maxDuration.TotalSeconds)];
            int length = 0;
            while (length < samples.Length)
            {
                int read = decoder.Decode(samples.AsSpan(length, Math.Min(sampleRate, samples.Length - length)));
                if (read <= 0) break;
                length += read;
            }

            float peak = 0;
            foreach (float sample in samples.AsSpan(0, length)) peak = Math.Max(peak, Math.Abs(sample));
            return new AudioRecording(WavEncoder.Encode(samples.AsSpan(0, length), sampleRate, 1),
                TimeSpan.FromSeconds(length / (double)sampleRate), peak);
        }
    }
}
