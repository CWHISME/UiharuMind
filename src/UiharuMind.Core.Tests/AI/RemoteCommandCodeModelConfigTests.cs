using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Command Code 预设的默认值：固定端点、公开目录地址、无写死变体表（候选走运行时模型目录，ADR 0092）。
/// </summary>
public class RemoteCommandCodeModelConfigTests
{
    [Fact]
    public void Defaults_PointAtCommandCodeEndpoints()
    {
        var config = new RemoteCommandCodeModelConfig();

        Assert.Equal("https://api.commandcode.ai/provider/v1/chat/completions", config.ModelPath);
        Assert.Equal("https://api.commandcode.ai/provider/v1/models", config.ModelCatalogUrl);
        Assert.Equal("https://commandcode.ai", config.WebsiteUrl);
    }

    [Fact]
    public void Defaults_NoHardcodedVariants_NoDefaultModelId()
    {
        var config = new RemoteCommandCodeModelConfig();

        Assert.Empty(config.ModelIdVariants);
        Assert.Equal("", config.ModelId);
        Assert.Equal(EApiFormat.ChatCompletions, config.ApiFormat);
    }

    [Fact]
    public void Registry_CreateDefault_ProducesCommandCodeConfig()
    {
        BaseRemoteModelConfig? config = RemoteModelConfigRegistry.CreateDefault(typeof(RemoteCommandCodeModelConfig));

        Assert.IsType<RemoteCommandCodeModelConfig>(config);
        Assert.Equal(typeof(RemoteCommandCodeModelConfig).Name, config!.ConfigType);
    }
}
