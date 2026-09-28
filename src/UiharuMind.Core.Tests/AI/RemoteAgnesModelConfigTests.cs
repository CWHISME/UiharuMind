using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// Agnes 思考参数映射:只有开/关两档,走 <c>chat_template_kwargs.enable_thinking</c>;
/// 顶层 <c>thinking</c>/<c>reasoning_effort</c> 经 litellm 网关直接 400,不能发。
/// </summary>
public class RemoteAgnesModelConfigTests
{
    private static Dictionary<string, JsonNode?> ExtraParams(EThinkingMode mode) =>
        new RemoteAgnesModelConfig { ThinkingMode = mode }.GetExtraParams()!
            .ToDictionary(p => p.Key, p => p.Value);

    [Fact]
    public void ThinkingLevels_AllCollapseToEnabled()
    {
        foreach (EThinkingMode mode in new[] { EThinkingMode.Light, EThinkingMode.Medium, EThinkingMode.High, EThinkingMode.Max })
        {
            var extra = ExtraParams(mode);

            Assert.True(extra["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
            Assert.DoesNotContain("thinking", extra);
            Assert.DoesNotContain("reasoning_effort", extra);
        }
    }

    [Fact]
    public void None_DisablesThinking()
    {
        var extra = ExtraParams(EThinkingMode.None);

        Assert.False(extra["chat_template_kwargs"]!["enable_thinking"]!.GetValue<bool>());
        Assert.DoesNotContain("thinking", extra);
    }

    [Fact]
    public void Default_OnlySendsMaxTokens()
    {
        var extra = ExtraParams(EThinkingMode.Default);

        Assert.DoesNotContain("chat_template_kwargs", extra);
        Assert.DoesNotContain("thinking", extra);
        Assert.Equal(65536, extra["max_tokens"]!.GetValue<int>());
    }
}
