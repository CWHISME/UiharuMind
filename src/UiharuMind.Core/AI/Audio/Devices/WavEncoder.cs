using System.Buffers.Binary;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 写最朴素的 PCM16 WAV：录音交给听写、裸 PCM 交给解码器时都要个文件头
/// </summary>
public static class WavEncoder
{
    private const int HeaderSize = 44;

    /// <summary>
    /// 浮点采样（-1..1，交错存放）→ PCM16 WAV，超出范围的截断
    /// </summary>
    /// <param name="samples">采样</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <returns>WAV 字节</returns>
    public static byte[] Encode(ReadOnlySpan<float> samples, int sampleRate, int channels)
    {
        byte[] wav = new byte[HeaderSize + samples.Length * 2];
        WriteHeader(wav, samples.Length * 2, sampleRate, channels);
        Span<byte> data = wav.AsSpan(HeaderSize);
        for (int i = 0; i < samples.Length; i++)
        {
            float clamped = Math.Clamp(samples[i], -1f, 1f);
            BinaryPrimitives.WriteInt16LittleEndian(data[(i * 2)..], (short)MathF.Round(clamped * short.MaxValue));
        }

        return wav;
    }

    /// <summary>
    /// 裸 PCM16（s16le）补上文件头
    /// </summary>
    /// <param name="pcm">采样字节</param>
    /// <param name="sampleRate">采样率</param>
    /// <param name="channels">声道数</param>
    /// <returns>WAV 字节</returns>
    public static byte[] WrapPcm16(ReadOnlySpan<byte> pcm, int sampleRate, int channels)
    {
        int length = pcm.Length & ~1; //半个采样丢掉
        byte[] wav = new byte[HeaderSize + length];
        WriteHeader(wav, length, sampleRate, channels);
        pcm[..length].CopyTo(wav.AsSpan(HeaderSize));
        return wav;
    }

    private static void WriteHeader(Span<byte> wav, int dataLength, int sampleRate, int channels)
    {
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav[4..], 36 + dataLength);
        "WAVEfmt "u8.CopyTo(wav[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(wav[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav[20..], 1); //PCM
        BinaryPrimitives.WriteInt16LittleEndian(wav[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(wav[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(wav[34..], 16);
        "data"u8.CopyTo(wav[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(wav[40..], dataLength);
    }
}
