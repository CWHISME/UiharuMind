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

    /// <summary>
    /// 读 WAV 文件头算时长：按块找 fmt 与 data，认不出返回 null
    /// </summary>
    /// <param name="wav">WAV 字节</param>
    /// <returns>时长</returns>
    public static TimeSpan? GetDuration(ReadOnlySpan<byte> wav)
    {
        if (wav.Length < 12 || !wav[..4].SequenceEqual("RIFF"u8) || !wav[8..12].SequenceEqual("WAVE"u8)) return null;
        int byteRate = 0;
        for (int offset = 12; offset + 8 <= wav.Length;)
        {
            ReadOnlySpan<byte> id = wav.Slice(offset, 4);
            int size = BinaryPrimitives.ReadInt32LittleEndian(wav[(offset + 4)..]);
            if (id.SequenceEqual("fmt "u8) && offset + 20 <= wav.Length)
                byteRate = BinaryPrimitives.ReadInt32LittleEndian(wav[(offset + 16)..]);
            else if (id.SequenceEqual("data"u8) && byteRate > 0)
            {
                // 流式写出的 WAV 常把 data 长度写成 0 或 -1，按实际剩下的字节算
                int length = size <= 0 || offset + 8 + size > wav.Length ? wav.Length - offset - 8 : size;
                return TimeSpan.FromSeconds(length / (double)byteRate);
            }

            if (size < 0) return null;
            offset += 8 + size + (size & 1);
        }

        return null;
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
