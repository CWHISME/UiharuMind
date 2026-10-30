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
