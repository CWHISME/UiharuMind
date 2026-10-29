using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 思考档位的取值约定与请求写法：档位不再固定 low/medium/high/max，
/// 字段名也不是统一的 <c>reasoning_effort</c>，按供应商族分派。
/// </summary>
public class ThinkingEffortsTests
{
    private static Dictionary<string, JsonNode?> ExtraParams(BaseRemoteModelConfig config) =>
        config.GetExtraParams()!.ToDictionary(pair => pair.Key, pair => pair.Value);

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("Default", "")]
    [InlineData("None", "none")]
    [InlineData("Light", "low")]
    [InlineData("Medium", "medium")]
    [InlineData("High", "high")]
    [InlineData("Max", "max")]
    [InlineData("xhigh", "xhigh")]
    [InlineData(" XHigh ", "xhigh")]
    public void Normalize_MapsLegacyEnumNamesAndKeepsWireValues(string? raw, string expected) =>
        Assert.Equal(expected, ThinkingEfforts.Normalize(raw));

    [Fact]
    public void ParseDeclared_KeepsDeclaredOrderAndDeduplicates()
    {
        Assert.Equal(["low", "medium", "xhigh"], ThinkingEfforts.ParseDeclared("low, medium ,xhigh,low"));
    }

    [Fact]
    public void ParseDeclared_FallsBackWhenModelDeclaresNothing()
    {
        Assert.Equal(ThinkingEfforts.Fallback, ThinkingEfforts.ParseDeclared(""));
        Assert.Equal(ThinkingEfforts.Fallback, ThinkingEfforts.ParseDeclared(null));
    }

    [Theory]
    [InlineData("https://open.bigmodel.cn/api/paas/v4/chat/completions", EThinkingFormat.ThinkingType)]
    [InlineData("https://api.z.ai/api/paas/v4/chat/completions", EThinkingFormat.ThinkingType)]
    [InlineData("https://api.deepseek.com/chat/completions", EThinkingFormat.ThinkingType)]
    [InlineData("https://api.agnes-ai.cn/v1/chat/completions", EThinkingFormat.ChatTemplate)]
    [InlineData("https://api.example.com/v1/chat/completions", EThinkingFormat.Effort)]
    [InlineData("", EThinkingFormat.Effort)]
    public void Detect_PicksFormatFromEndpoint(string modelPath, EThinkingFormat expected) =>
        Assert.Equal(expected, ThinkingFormats.Detect(modelPath));

    /// <summary>预设供应商显式声明格式，不靠地址猜</summary>
    [Fact]
    public void PresetProviders_DeclareTheirFormat()
    {
        Assert.Equal(EThinkingFormat.ChatTemplate, new RemoteAgnesModelConfig().ThinkingFormat);
        Assert.Equal(EThinkingFormat.ThinkingType, new RemoteZhipuModelConfig().ThinkingFormat);
        Assert.Equal(EThinkingFormat.ThinkingType, new RemoteDeepSeekModelConfig().ThinkingFormat);
    }

    /// <summary>聚合网关下个别模型可以单独声明写法，其余跟随供应商默认</summary>
    [Fact]
    public void PerModelPreset_OverridesProviderDefault()
    {
        Assert.Equal(EThinkingFormat.ChatTemplate, new PresetFormatConfig { ModelId = "special" }.ThinkingFormat);
        Assert.Equal(EThinkingFormat.Effort, new PresetFormatConfig { ModelId = "other" }.ThinkingFormat);
    }

    /// <summary>测试用：供应商默认是 Effort，其中一个模型在预设表里改成 ChatTemplate</summary>
    private sealed class PresetFormatConfig : BaseRemoteModelConfig
    {
        public override IReadOnlyDictionary<string, RemoteModelIdVariant> ModelIdVariants { get; } =
            new Dictionary<string, RemoteModelIdVariant>
            {
                ["special"] = new(ThinkingFormat: EThinkingFormat.ChatTemplate),
            };
    }

    [Fact]
    public void GetExtraParams_EffortFormat_SendsOnlyReasoningEffort()
    {
        var config = new RemoteModelConfig
        {
            ModelPath = "https://api.example.com/v1/chat/completions",
            ThinkingEffort = "xhigh",
        };

        var extra = ExtraParams(config);

        Assert.Equal("xhigh", extra["reasoning_effort"]!.GetValue<string>());
        Assert.DoesNotContain("thinking", extra);
        Assert.True(extra.ContainsKey("max_tokens"));
    }

    [Fact]
    public void GetExtraParams_EffortFormat_OffWritesNone()
    {
        var config = new RemoteModelConfig
        {
            ModelPath = "https://api.example.com/v1/chat/completions",
            ThinkingEffort = ThinkingEfforts.Off,
        };

        var extra = ExtraParams(config);

        Assert.Equal("none", extra["reasoning_effort"]!.GetValue<string>());
        Assert.DoesNotContain("thinking", extra);
    }

    [Fact]
    public void GetExtraParams_ThinkingTypeFormat_SendsSwitchAndLevel()
    {
        var config = new RemoteZhipuModelConfig { ModelId = "glm-5.3", ThinkingEffort = "high" };

        var extra = ExtraParams(config);

        Assert.Equal("enabled", extra["thinking"]!["type"]!.GetValue<string>());
        Assert.Equal("high", extra["reasoning_effort"]!.GetValue<string>());
    }

    [Fact]
    public void GetExtraParams_ThinkingTypeFormat_OffSendsNoLevel()
    {
        var config = new RemoteZhipuModelConfig { ThinkingEffort = ThinkingEfforts.Off };

        var extra = ExtraParams(config);

        Assert.Equal("disabled", extra["thinking"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("reasoning_effort", extra);
    }

    [Fact]
    public void GetExtraParams_AutoSendsNoThinkingParams()
    {
        var config = new RemoteZhipuModelConfig { ThinkingEffort = ThinkingEfforts.Auto };

        var extra = ExtraParams(config);

        Assert.Equal(["max_tokens"], extra.Keys.ToArray());
    }

    /// <summary>只有开关的模型：没有档位名可发，只把开关打开</summary>
    [Fact]
    public void ThinkingParamWriter_ToggleModelSendsSwitchWithoutLevel()
    {
        var parameters = ThinkingParamWriter.Write(
                EThinkingFormat.ThinkingType, new ThinkingSwitch(true, null), 4096)
            .ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("enabled", parameters["thinking"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("reasoning_effort", parameters);
    }

    [Fact]
    public void ResolveThinkingProfile_CustomProviderUsesFallbackAndIsNotDeclared()
    {
        ThinkingProfile profile =
            ModelCapabilityResolver.ResolveThinkingProfile(typeof(RemoteModelConfig), "some-unknown-model");

        Assert.Equal(ThinkingEfforts.Fallback, profile.Levels);
        Assert.False(profile.Declared);
    }

    /// <summary>Agnes 的模型只有开关，没有档位可选</summary>
    [Fact]
    public void ResolveThinkingProfile_ToggleModelHasNoLevels()
    {
        ThinkingProfile profile =
            ModelCapabilityResolver.ResolveThinkingProfile(typeof(RemoteAgnesModelConfig), "agnes-3.0-flash");

        Assert.Empty(profile.Levels);
        Assert.True(profile.Declared);
    }

    /// <summary>旧存档写的是 <c>"ThinkingMode": "Light"</c>，改名后必须还能读进 wire 值</summary>
    [Fact]
    public void ThinkingEffort_ReadsLegacyJsonKey()
    {
        const string json = """{"ThinkingMode":"Light","ModelId":"m"}""";

        var config = JsonSerializer.Deserialize<RemoteModelConfig>(json, SaveUtility.SaveJsonOptions);

        Assert.NotNull(config);
        Assert.Equal("low", ThinkingEfforts.Normalize(config.ThinkingEffort));
    }
}
