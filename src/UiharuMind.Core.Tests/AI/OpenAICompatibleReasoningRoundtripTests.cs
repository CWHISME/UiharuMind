using System.Text.Json.Nodes;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 请求改写：采样参数剥离（Kimi 类固定参数模型）与思考正文回填（DeepSeek 工具调用链）。
/// 前者只删四个采样键，后者按 tool_call id 把历史里的思考正文补回请求体。
/// </summary>
public class OpenAICompatibleReasoningRoundtripTests
{
    [Fact]
    public void RestoreReasoningContent_FillsMissingByCallId()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new Dictionary<string, string> { ["call-1"] = "先想好再调工具。" });

        Assert.Equal("先想好再调工具。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_MatchesAnyCallId()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"assistant","tool_calls":[{"id":"call-unknown"},{"id":"call-2"}]}]}""",
            new Dictionary<string, string> { ["call-2"] = "第二个调用的思考。" });

        Assert.Equal("第二个调用的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_KeepsExistingContent()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"assistant","reasoning_content":"已有的思考。","tool_calls":[{"id":"call-1"}]}]}""",
            new Dictionary<string, string> { ["call-1"] = "新的思考，不该覆盖。" });

        Assert.Equal("已有的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_ReplacesExplicitNull()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"assistant","reasoning_content":null,"tool_calls":[{"id":"call-1"}]}]}""",
            new Dictionary<string, string> { ["call-1"] = "补上的思考。" });

        Assert.Equal("补上的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_SkipsMessagesWithoutToolCalls()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"user","content":"你好"}]}""",
            new Dictionary<string, string> { ["call-1"] = "用不上的思考。" });

        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>只有要求回填的模型才去收集思考：别的模型一次都不调来源</summary>
    [Fact]
    public async Task ReasoningSource_IsOnlyInvokedForModelsThatRequireIt()
    {
        int invoked = await Task.Run(() =>
        {
            int count = 0;
            LlmRequestContext.PendingReasoningSource = () =>
            {
                count++;
                return new Dictionary<string, string> { ["c1"] = "思考" };
            };
            OpenAICompatibleRequestRewriter.For(new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } });
            OpenAICompatibleRequestRewriter.For(model: null);
            RequestRewrite rewrite = OpenAICompatibleRequestRewriter.For(
                new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3" } });
            Assert.NotNull(rewrite.ReasoningByCallId);
            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(1, invoked);
    }

    [Fact]
    public void KimiK3Preset_OmitsSamplingParamsAndRequiresReasoningRoundtrip()
    {
        var config = new RemoteSensenovaModelConfig();
        var preset = config.ModelIdVariants["kimi-k3"];

        Assert.True(preset.OmitSamplingParams);
        Assert.True(preset.RequiresReasoningContentRoundtrip);
    }

    [Fact]
    public void KimiK3ModelInfo_FollowsPresetWithoutOverride()
    {
        var info = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3" },
        };

        Assert.True(info.RequiresReasoningContentRoundtrip);
    }

    private static JsonObject RestoreFirstMessage(string json, Dictionary<string, string> reasoning)
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(json, new RequestRewrite(null, false, reasoning, false));
        return (JsonObject)JsonNode.Parse(rewritten ?? json)!["messages"]![0]!;
    }
}
