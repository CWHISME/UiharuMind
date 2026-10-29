using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 思考档位的取值约定:不再固定 low/medium/high/max 四档,
/// 模型声明什么就发什么,旧存档的枚举名要能读成 wire 值。
/// </summary>
public class ThinkingEffortsTests
{
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

    [Fact]
    public void GetExtraParams_SendsDeclaredValueVerbatim()
    {
        var config = new RemoteModelConfig { ThinkingEffort = "xhigh" };

        var extra = config.GetExtraParams()!.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("xhigh", extra["reasoning_effort"]!.GetValue<string>());
        Assert.Equal("enabled", extra["thinking"]!["type"]!.GetValue<string>());
        Assert.True(extra.ContainsKey("max_tokens"));
    }

    [Fact]
    public void GetExtraParams_OffDisablesThinking()
    {
        var config = new RemoteModelConfig { ThinkingEffort = ThinkingEfforts.Off };

        var extra = config.GetExtraParams()!.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal("disabled", extra["thinking"]!["type"]!.GetValue<string>());
        Assert.DoesNotContain("reasoning_effort", extra);
    }

    [Fact]
    public void GetExtraParams_AutoSendsNoThinkingParams()
    {
        var config = new RemoteModelConfig { ThinkingEffort = ThinkingEfforts.Auto };

        var extra = config.GetExtraParams()!.ToDictionary(pair => pair.Key, pair => pair.Value);

        Assert.Equal(["max_tokens"], extra.Keys.ToArray());
    }

    [Fact]
    public void ResolveEffortOptions_CustomProviderUsesFallback()
    {
        Assert.Equal(
            ThinkingEfforts.Fallback,
            ModelCapabilityResolver.ResolveEffortOptions(typeof(RemoteModelConfig), "some-unknown-model"));
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
