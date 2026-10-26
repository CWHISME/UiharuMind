using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// OpenCode 预设（变体）：opencode.ai 的推理网关，目录形状最简（仅 id，无 name/context/endpoints）。
/// 用来实测通用模型目录流程的第二家真实服务商（ADR 0092）。
/// </summary>
public class RemoteOpenCodeModelConfigTests
{
    [Fact]
    public void Defaults_PointAtOpenCodeGateway()
    {
        var config = new RemoteOpenCodeModelConfig();

        Assert.Equal("https://opencode.ai/inference/openai/v1/chat/completions", config.ModelPath);
        Assert.Equal("https://opencode.ai/inference/v1/models", config.ModelCatalogUrl);
        Assert.Equal("https://opencode.ai", config.WebsiteUrl);
    }

    [Fact]
    public void Defaults_NoHardcodedVariants_NoDefaultModelId()
    {
        var config = new RemoteOpenCodeModelConfig();

        Assert.Empty(config.ModelIdVariants);
        Assert.Equal("", config.ModelId);
        Assert.Equal(EApiFormat.ChatCompletions, config.ApiFormat);
    }
}
