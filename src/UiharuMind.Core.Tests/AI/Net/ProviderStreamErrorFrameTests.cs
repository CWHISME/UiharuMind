using System.Text;
using UiharuMind.Core.AI.Net.Errors;
using UiharuMind.Core.AI.Net.Response;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 上游在流里回的错误帧。
///
/// <para>
/// 实测原样（commandcode 网关，HTTP 仍是 200）：
/// <c>data: {"id":"d926c995-…","error":{"code":400,"message":"Error processing stream start"},
/// "choices":[{"index":0,"delta":{"content":""},"finish_reason":"error"}]}</c>
/// </para>
/// <para>
/// 为什么这条判据值得钉死：不认出它，这一帧会被当成「正常结束的空回复」——流照常收尾、
/// 没有工具调用、没有异常，上层于是把一次链路故障记成「模型什么都没说」，
/// 派活方看到空白回执立刻重发（实机：子代理连续三轮 <c>elapsed=45s report=10 chars</c>）。
/// </para>
/// </summary>
public class ProviderStreamErrorFrameTests
{
    [Fact]
    public void ErrorObjectWithNumericCode_IsParsed()
    {
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"id\":\"d926c995-3d99-495e-a932-8b7b601a2632\","
            + "\"object\":\"chat.completion.chunk\","
            + "\"error\":{\"code\":400,\"message\":\"Error processing stream start\"},"
            + "\"choices\":[{\"index\":0,\"delta\":{\"content\":\"\"},\"finish_reason\":\"error\"}]}");

        Assert.True(ProviderStreamErrorParser.TryParse(line, out ProviderStreamErrorException? error));
        Assert.NotNull(error);
        Assert.Equal(400, error!.Code);
        Assert.Contains("Error processing stream start", error.Message);
        Assert.Contains("d926c995", error.Message);
        // 4xx 原样重发只会再白付一次上下文
        Assert.False(error.Transient);
    }

    [Fact]
    public void StringCode_IsParsed()
    {
        // 商汤那类把 code 写成字符串、且用的是自家码值的网关：认得出，但不属于 HTTP 4xx 语义，
        // 所以按「不明」处理——给两次重发机会（分不清就别赌它是判决）
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"error\":{\"message\":\"engine busy\",\"code\":\"9\"}}");

        Assert.True(ProviderStreamErrorParser.TryParse(line, out ProviderStreamErrorException? error));
        Assert.Equal(9, error!.Code);
        Assert.True(error.Transient);
    }

    [Fact]
    public void FinishReasonErrorWithoutErrorObject_IsStillAnError()
    {
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"id\":\"x\",\"choices\":[{\"index\":0,\"delta\":{},\"finish_reason\":\"error\"}]}");

        Assert.True(ProviderStreamErrorParser.TryParse(line, out ProviderStreamErrorException? error));
        Assert.Null(error!.Code);
        Assert.True(error.FinishReasonOnly);
        Assert.True(error.Transient);
    }

    [Fact]
    public void NormalFrame_IsNotAnError()
    {
        // 正常帧里本来就可能有 "finish_reason":"stop" 与 content，别把正文当错误帧
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"id\":\"a\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"说 error 也正常\"},"
            + "\"finish_reason\":\"stop\"}],\"usage\":{\"total_tokens\":12}}");

        Assert.False(ProviderStreamErrorParser.TryParse(line, out _));
    }

    [Fact]
    public void NonDataLine_IsNotAnError()
    {
        Assert.False(ProviderStreamErrorParser.TryParse(Encoding.UTF8.GetBytes("event: ping"), out _));
        Assert.False(ProviderStreamErrorParser.TryParse(Encoding.UTF8.GetBytes("data: [DONE]"), out _));
    }

    [Fact]
    public void Stream_ThrowsWhenProviderSendsErrorFrame()
    {
        // 端到端：流读到这一帧必须抛，而不是悄悄把空回复交给下游
        string body = "data: {\"choices\":[{\"index\":0,\"delta\":{\"content\":\"hi\"},\"finish_reason\":null}]}\n\n"
                      + "data: {\"error\":{\"code\":400,\"message\":\"Error processing stream start\"}}\n\n";
        using MemoryStream inner = new(Encoding.UTF8.GetBytes(body));
        using SseSanitizingStream stream = new(inner);

        var thrown = Assert.Throws<ProviderStreamErrorException>(() => Drain(stream));
        Assert.Equal(400, thrown.Code);
    }

    private static void Drain(Stream stream)
    {
        byte[] buffer = new byte[4096];
        while (stream.Read(buffer, 0, buffer.Length) > 0)
        {
        }
    }

    /// <summary>码写成 400.0 时仍要认成 400：认不出会当成「不明抖动」，白白重发两次</summary>
    [Fact]
    public void FractionalCode_IsRoundedToAnInt()
    {
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"error\":{\"code\":400.0,\"message\":\"Error processing stream start\"}}");

        Assert.True(ProviderStreamErrorParser.TryParse(line, out ProviderStreamErrorException? error));
        Assert.Equal(400, error!.Code);
        Assert.False(error.Transient);
        Assert.Contains("Error processing stream start", error.Message);
    }

    /// <summary>
    /// 码是整个对象时，跳过它但不能丢掉 message：留在对象内部的话，
    /// 外层会把这个对象的 EndObject 当成 error 对象的收尾，错因一个都读不到
    /// </summary>
    [Fact]
    public void ObjectCode_IsSkippedWithoutLosingTheMessage()
    {
        byte[] line = Encoding.UTF8.GetBytes(
            "data: {\"error\":{\"code\":{\"value\":400},\"message\":\"bad request\"}}");

        Assert.True(ProviderStreamErrorParser.TryParse(line, out ProviderStreamErrorException? error));
        Assert.Null(error!.Code);
        Assert.Contains("bad request", error.Message);
    }
}
