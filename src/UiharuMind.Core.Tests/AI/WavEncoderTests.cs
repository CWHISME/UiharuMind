using System.Buffers.Binary;
using System.Net;
using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 录音编成 WAV、裸 PCM 补文件头；音频服务的错误体拆成状态码与种类
/// </summary>
public class WavEncoderTests
{
    [Fact]
    public void Encode_WritesPcm16Header_AndClampsSamples()
    {
        byte[] wav = WavEncoder.Encode([0f, 1f, -1f, 2f], 16000, 1);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20))); //PCM
        Assert.Equal(1, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)));
        Assert.Equal(16000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)));
        Assert.Equal(32000, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(28)));
        Assert.Equal(8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(46)));
        Assert.Equal(-short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(48)));
        Assert.Equal(short.MaxValue, BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(50))); //超出范围截断
    }

    [Fact]
    public void WrapPcm16_DropsHalfSample()
    {
        byte[] wav = WavEncoder.WrapPcm16([1, 2, 3], 24000, 1);

        Assert.Equal(46, wav.Length);
        Assert.Equal(2, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
    }

    [Theory]
    [InlineData("""{"error":{"message":"cannot load","type":"insufficient_memory"}}""", "cannot load", "insufficient_memory")]
    [InlineData("""{"error":{"message":"bad voice"}}""", "bad voice", null)]
    [InlineData("oops", "Audio request failed: 503 oops", null)]
    public void ServiceError_ParsesOpenAiErrorBody(string body, string message, string? type)
    {
        AudioServiceException e = AudioServiceException.FromResponse(HttpStatusCode.ServiceUnavailable, body);

        Assert.Equal(message, e.Message);
        Assert.Equal(type, e.ErrorType);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, e.StatusCode);
    }
}
