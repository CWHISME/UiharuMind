using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 接口地址与 wire 格式的换算：地址是唯一事实，格式从尾段反推。
/// 这张表错了，界面给出的候选就和运行时真打的地址不是同一个。
/// </summary>
public class RemoteEndpointsTests
{
    [Theory]
    [InlineData("https://api.commandcode.ai/provider/v1/chat/completions", EApiFormat.ChatCompletions)]
    [InlineData("https://api.commandcode.ai/provider/v1/responses", EApiFormat.Responses)]
    [InlineData("https://api.commandcode.ai/provider/v1/messages", EApiFormat.Messages)]
    // 尾斜杠（复制粘贴、代理重写常见）不该改变结论
    [InlineData("https://api.commandcode.ai/provider/v1/messages/", EApiFormat.Messages)]
    [InlineData("  https://api.commandcode.ai/provider/v1/responses  ", EApiFormat.Responses)]
    // 查询串不影响路径判定
    [InlineData("https://api.commandcode.ai/provider/v1/messages?beta=1", EApiFormat.Messages)]
    // 路径大小写不敏感
    [InlineData("https://api.commandcode.ai/provider/v1/Messages", EApiFormat.Messages)]
    public void FormatOf_ReadsTheTrailingEndpoint(string address, EApiFormat expected)
    {
        Assert.Equal(expected, RemoteEndpoints.FormatOf(address));
    }

    /// <summary>
    /// 认不出就是「不知道」，不是 chat：只填到基址（<c>/v1</c>、裸域）时运行时按格式补后缀，
    /// 那种地址可以是任何一条线，判成 chat 会让编辑窗口一存就把 messages 的模型翻掉
    /// </summary>
    [Theory]
    [InlineData("https://proxy.example")]
    [InlineData("https://proxy.example/v1")]
    [InlineData("")]
    [InlineData("not a url")]
    public void FormatOf_UnknownAddress_IsNull(string address)
    {
        Assert.Null(RemoteEndpoints.FormatOf(address));
    }

    [Theory]
    [InlineData("https://api.commandcode.ai/provider/v1/chat/completions", EApiFormat.Messages,
        "https://api.commandcode.ai/provider/v1/messages")]
    [InlineData("https://api.deepseek.com/chat/completions", EApiFormat.Messages,
        "https://api.deepseek.com/messages")]
    // 裸域走运行时那套补全：补 /v1 再补后缀
    [InlineData("https://proxy.example", EApiFormat.Messages, "https://proxy.example/v1/messages")]
    [InlineData("https://proxy.example/v1", EApiFormat.Responses, "https://proxy.example/v1/responses")]
    // 已是本协议端点：一个字节都不动
    [InlineData("https://proxy.example/v1/messages", EApiFormat.Messages, "https://proxy.example/v1/messages")]
    public void EndpointFor_NormalizesLikeTheRuntime(string address, EApiFormat format, string expected)
    {
        Assert.Equal(expected, RemoteEndpoints.EndpointFor(address, format));
    }

    /// <summary>不成形的地址原样返回，不抛（这是 public API，别的入口也会调）</summary>
    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    public void EndpointFor_Unparsable_ReturnsInput(string address)
    {
        Assert.Equal(address, RemoteEndpoints.EndpointFor(address, EApiFormat.Messages));
    }

    /// <summary>目录声明了什么就报什么，三条都声明就三条都给（推荐哪条由界面定）</summary>
    [Fact]
    public void SupportedApiFormats_ReportsDeclaredEndpoints()
    {
        var entry = new ModelCatalogEntry
        {
            Id = "gpt-6-astra",
            SupportedEndpoints = ["/chat/completions", "/responses"],
        };

        Assert.Equal([EApiFormat.ChatCompletions, EApiFormat.Responses], entry.SupportedApiFormats());
    }

    [Fact]
    public void SupportedApiFormats_EmptyWhenCatalogSaysNothing()
    {
        Assert.Empty(new ModelCatalogEntry { Id = "beam-free" }.SupportedApiFormats());
    }

    /// <summary>
    /// OpenCode 的三条线不在同一段前缀上（openai / anthropic），供应商自己声明，
    /// 通用规则换尾段换不出来
    /// </summary>
    [Theory]
    [InlineData(EApiFormat.ChatCompletions, "https://opencode.ai/inference/openai/v1/chat/completions")]
    [InlineData(EApiFormat.Responses, "https://opencode.ai/inference/openai/v1/responses")]
    [InlineData(EApiFormat.Messages, "https://opencode.ai/inference/anthropic/v1/messages")]
    public void OpenCodeConfig_DeclaresEachLine(EApiFormat format, string expected)
    {
        Assert.Equal(expected, new RemoteOpenCodeModelConfig().EndpointFor(format));
    }

    /// <summary>
    /// 没声明布局的供应商返回空串，交给通用规则。CommandCode 属于这一类：
    /// 三条线同在 <c>/provider/v1</c>（commandcode.ai/docs/provider），换尾段就是对的
    /// </summary>
    [Fact]
    public void OtherProviders_DeclareNothing()
    {
        Assert.Equal("", new RemoteDeepSeekModelConfig().EndpointFor(EApiFormat.Messages));
        Assert.Equal("", new RemoteCommandCodeModelConfig().EndpointFor(EApiFormat.Messages));
        Assert.Equal("", new RemoteModelConfig().EndpointFor(EApiFormat.ChatCompletions));
    }
}
