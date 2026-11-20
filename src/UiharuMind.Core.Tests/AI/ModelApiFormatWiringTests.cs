using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// wire 格式声明（<see cref="ILlmModel.ApiFormat"/>）的接线：
/// 配置侧存的值必须能透过接口读到——不然声明了格式、链路上却按默认的 chat 走。
/// </summary>
public class ModelApiFormatWiringTests
{
    [Fact]
    public void Interface_ReadsConfiguredFormat()
    {
        var config = new RemoteModelConfig { ApiFormat = EApiFormat.Responses };

        // 经接口读：必须是配置里存的那个，不是接口的默认值
        ILlmModel model = config;

        Assert.Equal(EApiFormat.Responses, model.ApiFormat);
    }

    /// <summary>
    /// 运行时用的是 <see cref="RemoteModelInfo"/> 包装层（模型列表、建客户端的入参都是它），
    /// 不是配置类本身。接口上 ApiFormat 的默认实现是 ChatCompletions——包装层不委托配置就只能读到 chat，
    /// 于是 messages 端点的请求被归一成 chat 端点发出去（CommandCode Claude 的 400 就是这么来的）。
    /// </summary>
    [Theory]
    [InlineData(EApiFormat.ChatCompletions)]
    [InlineData(EApiFormat.Messages)]
    [InlineData(EApiFormat.Responses)]
    public void RemoteModelInfo_ReadsConfiguredFormat_NotTheInterfaceDefault(EApiFormat format)
    {
        ILlmModel model = new RemoteModelInfo { Config = new RemoteModelConfig { ApiFormat = format } };

        Assert.Equal(format, model.ApiFormat);
    }

    [Fact]
    public void Interface_DefaultsToChatCompletions_WhenNotOverridden()
    {
        ILlmModel model = new RemoteModelConfig();

        Assert.Equal(EApiFormat.ChatCompletions, model.ApiFormat);
    }

    [Fact]
    public void Responses_RoundTripsThroughArchive()
    {
        var config = new RemoteModelConfig { ApiFormat = EApiFormat.Responses };

        string json = SaveUtility.SaveToString<BaseRemoteModelConfig>(config);
        RemoteModelConfig restored = SaveUtility.LoadFromString<RemoteModelConfig>(json);

        Assert.Equal(EApiFormat.Responses, restored.ApiFormat);
    }
}
