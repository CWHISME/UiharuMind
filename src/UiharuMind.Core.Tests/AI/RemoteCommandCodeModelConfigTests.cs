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

    /// <summary>
    /// 实测：CommandCode 上的 MiMo 输出上限是 131072，配置里存着 384000 时整笔请求被上游 400 掉，
    /// 而错误正文只有一句 <c>Param Incorrect</c>，看不出是哪个参数。发之前钳到声明上限。
    /// </summary>
    [Fact]
    public void GetExtraParams_MaxTokensOverDeclaredCap_IsClamped()
    {
        var config = new RemoteCommandCodeModelConfig
        {
            ModelId = "xiaomi/mimo-v2.6-flash",
            MaxTokens = 384000,
        };

        Assert.Equal(131072, MaxTokensOf(config));
    }

    /// <summary>用户填的值不被就地改写：编辑窗口照旧显示他自己填的那个数</summary>
    [Fact]
    public void GetExtraParams_Clamping_DoesNotRewriteConfig()
    {
        var config = new RemoteCommandCodeModelConfig
        {
            ModelId = "xiaomi/mimo-v2.6-flash",
            MaxTokens = 384000,
        };

        _ = MaxTokensOf(config);

        Assert.Equal(384000, config.MaxTokens);
    }

    /// <summary>没超上限的值原样发，不无故缩预算</summary>
    [Fact]
    public void GetExtraParams_MaxTokensWithinCap_IsKept()
    {
        var config = new RemoteCommandCodeModelConfig
        {
            ModelId = "xiaomi/mimo-v2.6-flash",
            MaxTokens = 32000,
        };

        Assert.Equal(32000, MaxTokensOf(config));
    }

    /// <summary>表里没有的模型不猜，按用户填的值发</summary>
    [Fact]
    public void GetExtraParams_UnknownModel_SendsConfiguredValue()
    {
        var config = new RemoteCommandCodeModelConfig
        {
            ModelId = "local/definitely-not-a-model",
            MaxTokens = 384000,
        };

        Assert.Equal(384000, MaxTokensOf(config));
    }

    private static int? MaxTokensOf(BaseRemoteModelConfig config)
    {
        foreach (KeyValuePair<string, System.Text.Json.Nodes.JsonNode?> pair in config.GetExtraParams() ?? [])
        {
            if (pair.Key == "max_tokens" && pair.Value is not null) return pair.Value.GetValue<int>();
        }

        return null;
    }
}
