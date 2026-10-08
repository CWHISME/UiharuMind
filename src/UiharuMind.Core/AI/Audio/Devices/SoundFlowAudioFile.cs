using SoundFlow.Components;
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

    /// <summary>
    /// 把一段合成结果整段解码成单声道浮点采样（按需重采样）
    /// </summary>
    /// <param name="audio">合成结果（流读完即可，释放仍归调用方）</param>
    /// <param name="sampleRate">要的采样率</param>
    /// <returns>采样</returns>
    /// <exception cref="NotSupportedException">格式解不了</exception>
    public static float[] Decode(AudioSynthesisResult audio, int sampleRate)
    {
        using MemoryStream buffer = new();
        audio.Audio.CopyTo(buffer);
        byte[] bytes = audio.Format == EAudioFormat.Pcm16
            ? WavEncoder.WrapPcm16(buffer.GetBuffer().AsSpan(0, (int)buffer.Length),
                audio.SampleRateHz ?? throw new ArgumentException("Pcm16 audio needs SampleRateHz.", nameof(audio)), 1)
            : buffer.ToArray();
        string format = audio.Format switch
        {
            EAudioFormat.Mp3 => "mp3",
            EAudioFormat.Wav or EAudioFormat.Pcm16 => "wav",
            _ => throw new NotSupportedException($"Cannot decode {audio.Format}.")
        };

        using MemoryStream stream = new(bytes, false);
        using ISoundDecoder decoder = SoundFlowRuntime.Engine.CreateDecoder(stream, format,
            SoundFlowRuntime.FloatFormat(sampleRate, 1));
        List<float> samples = [];
        float[] chunk = new float[sampleRate];
        int read;
        while ((read = decoder.Decode(chunk)) > 0) samples.AddRange(chunk.AsSpan(0, read));
        return samples.ToArray();
    }

    /// <summary>
    /// 变速不变调（WSOLA，与播放时变速同一套）
    /// </summary>
    /// <param name="samples">单声道采样</param>
    /// <param name="speed">倍率；1 原样返回</param>
    /// <returns>变速后的采样</returns>
    public static float[] Stretch(float[] samples, float speed)
    {
        if (speed == 1f || samples.Length == 0) return samples;
        WsolaTimeStretcher stretcher = new(1, speed, WsolaConfig.FromPreset(WsolaPerformancePreset.Fast));
        List<float> output = [];
        float[] buffer = new float[8192];
        int position = 0;
        while (position < samples.Length)
        {
            int written = stretcher.Process(samples.AsSpan(position), buffer, out int consumed, out _);
            output.AddRange(buffer.AsSpan(0, written));
            if (consumed == 0 && written == 0) break;
            position += consumed;
        }

        int tail;
        while ((tail = stretcher.Flush(buffer)) > 0) output.AddRange(buffer.AsSpan(0, tail));
        return output.ToArray();
    }
}
