using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 协议事实表：端点地址归一、地址反查、以及「这条线要不要洗响应」。
///
/// 远程配置允许填完整接口，也允许只填根路径（<c>https://x</c> / <c>https://x/v1</c>），
/// 三种形态必须收敛到同一条协议的端点——漏一种就是 <c>/v1/v1/chat/completions</c> 这种 404。
/// 三条协议走<b>同一套</b>归一逻辑，只是后缀不同；这里按协议各验一遍，防止将来只改一条。
/// </summary>
public class WireProtocolTests
{
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void Chat_ConvergesEveryShapeToTheChatEndpoint(string address, string expected)
    {
        Assert.Equal(expected, WireProtocols.Chat.Resolve(address).Uri.ToString());
    }

    /// <summary>
    /// responses 与 messages 的归一规则与 chat 相同，只是后缀换了。
    /// 已填完整路径时不能再补一层（否则打成 <c>/responses/responses</c>）。
    /// </summary>
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/v1/responses", "https://api.example.com/v1/responses")]
    public void Responses_ConvergesEveryShapeToTheResponsesEndpoint(string address, string expected)
    {
        Assert.Equal(expected, WireProtocols.Responses.Resolve(address).Uri.ToString());
    }

    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/messages")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/messages")]
    [InlineData("https://api.example.com/v1/messages", "https://api.example.com/v1/messages")]
    public void Messages_ConvergesEveryShapeToTheMessagesEndpoint(string address, string expected)
    {
        Assert.Equal(expected, WireProtocols.Messages.Resolve(address).Uri.ToString());
    }

    /// <summary>带查询串时路径照补、查询串保留（自建网关常带 api-version 之类）</summary>
    [Fact]
    public void Resolve_KeepsQueryString()
    {
        Assert.Equal("https://api.example.com/v1/chat/completions?api-version=2",
            WireProtocols.Chat.Resolve("https://api.example.com/v1?api-version=2").Uri.ToString());
        Assert.Equal("https://api.example.com/v1/responses?api-version=2",
            WireProtocols.Responses.Resolve("https://api.example.com/v1?api-version=2").Uri.ToString());
    }

    /// <summary>三条线的判据互不重叠：一条协议的端点不会被认成另一条</summary>
    [Theory]
    [InlineData("/v1/chat/completions", EApiFormat.ChatCompletions)]
    [InlineData("/v1/Chat/Completions", EApiFormat.ChatCompletions)]
    [InlineData("/v1/responses", EApiFormat.Responses)]
    [InlineData("/v1/Responses", EApiFormat.Responses)]
    [InlineData("/v1/messages", EApiFormat.Messages)]
    public void Detect_IdentifiesTheWireFormat(string path, EApiFormat expected)
    {
        Assert.Equal(expected, WireProtocols.Detect(new Uri("https://api.example.com" + path))?.Format);
    }

    /// <summary>认不出的路径（模型目录、生图）不返回协议——那种路径由各自调用方负责，不该被当成对话端点</summary>
    [Theory]
    [InlineData("/v1/models")]
    [InlineData("/v1/images/generations")]
    public void Detect_ReturnsNullForNonConversationPaths(string path)
    {
        Assert.Null(WireProtocols.Detect(new Uri("https://api.example.com" + path)));
    }

    /// <summary>
    /// 只有 chat 的响应需要逐行修。responses 与 messages 的响应形状由各自 SDK 解析，
    /// 套上 chat 那层会把正常收尾误报成「连接被掐」——这条判据是那个 bug 的唯一来源
    /// </summary>
    [Fact]
    public void OnlyChatSanitizesItsResponse()
    {
        Assert.True(WireProtocols.Chat.SanitizesResponse);
        Assert.False(WireProtocols.Responses.SanitizesResponse);
        Assert.False(WireProtocols.Messages.SanitizesResponse);
    }

    /// <summary>格式与协议一一对应；认不出的值退到 chat（枚举默认值就是 chat）</summary>
    [Fact]
    public void For_MapsEveryFormat()
    {
        Assert.Same(WireProtocols.Chat, WireProtocols.For(EApiFormat.ChatCompletions));
        Assert.Same(WireProtocols.Responses, WireProtocols.For(EApiFormat.Responses));
        Assert.Same(WireProtocols.Messages, WireProtocols.For(EApiFormat.Messages));
        Assert.Same(WireProtocols.Chat, WireProtocols.For((EApiFormat)99));
    }
}
