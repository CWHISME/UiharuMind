using UiharuMind.Core.AI.Net.Transport;

namespace UiharuMind.Core.Tests.AI.Net;

/// <summary>
/// 端点地址归一：远程配置允许填完整接口，也允许只填根路径（<c>https://x</c> / <c>https://x/v1</c>），
/// 三种形态必须收敛到同一个 chat completions 地址——漏一种就是 <c>/v1/v1/chat/completions</c> 这种 404。
/// </summary>
public class ChatCompletionUriTests
{
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/chat/completions")]
    [InlineData("https://api.example.com/v1/chat/completions", "https://api.example.com/v1/chat/completions")]
    public void Resolve_ConvergesEveryShapeToTheChatEndpoint(string address, string expected)
    {
        Assert.Equal(expected, ChatCompletionUri.Resolve(address).Uri.ToString());
    }

    /// <summary>带查询串时路径照补、查询串保留（自建网关常带 api-version 之类）</summary>
    [Fact]
    public void Resolve_KeepsQueryString()
    {
        Assert.Equal("https://api.example.com/v1/chat/completions?api-version=2",
            ChatCompletionUri.Resolve("https://api.example.com/v1?api-version=2").Uri.ToString());
    }

    /// <summary>只有聊天补全的响应要走清洗；模型目录、生图这些路径不能被当成 chat 处理</summary>
    [Theory]
    [InlineData("/v1/chat/completions", true)]
    [InlineData("/v1/Chat/Completions", true)]
    [InlineData("/v1/models", false)]
    [InlineData("/v1/responses", false)]
    public void IsChatCompletions_OnlyMatchesTheChatPath(string path, bool expected)
    {
        Assert.Equal(expected, ChatCompletionUri.IsChatCompletions(new Uri("https://api.example.com" + path)));
    }

    /// <summary>
    /// responses 端点的地址归一：与 chat 同样的三种形态收敛，只是结尾换成了 <c>/responses</c>。
    /// 已填完整路径时不能再补一层（否则打成 <c>/responses/responses</c>）。
    /// </summary>
    [Theory]
    [InlineData("https://api.example.com", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/v1", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/v1/", "https://api.example.com/v1/responses")]
    [InlineData("https://api.example.com/v1/responses", "https://api.example.com/v1/responses")]
    public void ResolveResponses_ConvergesEveryShapeToTheResponsesEndpoint(string address, string expected)
    {
        Assert.Equal(expected, ChatCompletionUri.ResolveResponses(address).Uri.ToString());
    }

    /// <summary>responses 地址带查询串时路径照补、查询串保留</summary>
    [Fact]
    public void ResolveResponses_KeepsQueryString()
    {
        Assert.Equal("https://api.example.com/v1/responses?api-version=2",
            ChatCompletionUri.ResolveResponses("https://api.example.com/v1?api-version=2").Uri.ToString());
    }

    /// <summary>判是不是 responses 线：chat 与模型目录都不算</summary>
    [Theory]
    [InlineData("/v1/responses", true)]
    [InlineData("/v1/Responses", true)]
    [InlineData("/v1/chat/completions", false)]
    [InlineData("/v1/models", false)]
    public void IsResponses_OnlyMatchesTheResponsesPath(string path, bool expected)
    {
        Assert.Equal(expected, ChatCompletionUri.IsResponses(new Uri("https://api.example.com" + path)));
    }

    /// <summary>
    /// 两条线互不误判：填了 responses 的地址不能被当成 chat 走（否则请求体会发成 messages 形状）。
    /// </summary>
    [Fact]
    public void Responses_IsNotMistakenForChatCompletions()
    {
        var uri = ChatCompletionUri.ResolveResponses("https://api.example.com/v1").Uri;

        Assert.True(ChatCompletionUri.IsResponses(uri));
        Assert.False(ChatCompletionUri.IsChatCompletions(uri));
    }
}
