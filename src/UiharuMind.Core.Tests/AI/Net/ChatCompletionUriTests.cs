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
}
