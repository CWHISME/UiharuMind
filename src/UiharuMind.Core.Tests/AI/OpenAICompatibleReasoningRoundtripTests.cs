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

    /// <summary>回填键名跟随模型字段（如 reasoning），不写死 reasoning_content</summary>
    [Fact]
    public void RestoreReasoningContent_UsesConfiguredFieldName()
    {
        JsonObject message = RestoreFirstMessage(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new Dictionary<string, string> { ["call-1"] = "先想好再调工具。" },
            reasoningField: "reasoning");

        Assert.Equal("先想好再调工具。", message["reasoning"]!.GetValue<string>());
        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>roundtrip=null(自动)与 true 都去收集(收集器扫不到就空);显式 false 与无模型跳过</summary>
    [Fact]
    public async Task ReasoningSource_IsCollectedForAutoOrRequired_SkippedForExplicitOff()
    {
        int invoked = await Task.Run(() =>
        {
            int count = 0;
            LlmRequestContext.PendingReasoningSource = () =>
            {
                count++;
                return new Dictionary<string, string> { ["c1"] = "思考" };
            };
            // 显式关(商汤自研,官方推荐不回传):不收集
            OpenAICompatibleRequestRewriter.For(new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } });
            // 无模型:不收集
            OpenAICompatibleRequestRewriter.For(model: null);
            // 自动(未声明):收集
            OpenAICompatibleRequestRewriter.For(new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } });
            // 必须回填(kimi-k3):收集
            RequestRewrite rewrite = OpenAICompatibleRequestRewriter.For(
                new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3" } });
            Assert.NotNull(rewrite.ReasoningByCallId);
            return count;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, invoked);
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

    /// <summary>
    /// 从模型配置一路查到请求体里的键名（此前只分别测了「观测落盘」和「按给定键名改写」，
    /// 中间的接线没人钉）：观测值 → 预设 → 默认，且空串绝不能拼进请求体。
    /// </summary>
    [Fact]
    public void ReasoningField_ResolvesObservedThenPresetThenDefault()
    {
        var observed = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } };
        observed.Config.ObservedThinkingFields["glm-5.2"] = "reasoning_text";
        Assert.Equal("reasoning_text", OpenAICompatibleRequestRewriter.For(observed).ReasoningField);

        // 同端点另一模型的预设声明照常生效（观测值只属于各自的 ModelId）
        var preseted = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" },
        };
        Assert.Equal("reasoning", OpenAICompatibleRequestRewriter.For(preseted).ReasoningField);

        Assert.Equal("reasoning_content", OpenAICompatibleRequestRewriter.For(new RemoteModelInfo()).ReasoningField);
        Assert.Equal("reasoning_content", OpenAICompatibleRequestRewriter.For(model: null).ReasoningField);
        // 违反契约给了空串的实现：退回默认，而不是写一个 `"": "..."` 进请求体
        Assert.Equal("reasoning_content", OpenAICompatibleRequestRewriter.For(new BlankFieldModel()).ReasoningField);
    }

    /// <summary>思考字段名给空串的模型（接口契约说该给生效键名，实现可能给空）</summary>
    private sealed class BlankFieldModel : ILlmModel
    {
        public string ModelName => "blank";
        public string ModelPath => "";
        public bool IsVision => false;
        public string ModelDescription => "";
        public string ModelId => "blank";
        public int Port => 0;
        public string ThinkingField => "";
    }

    /// <summary>
    /// 没有思考正文可回传时补空串：DeepSeek 类要求「思考模式下带 tool_calls 的助手消息必须含
    /// reasoning_content」，字段缺了直接 400——历史里关思考档位跑出来的工具调用就是这种。
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_NoThinkingOnToolCallMessage_WritesEmptyString()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, null, false, FillEmptyReasoning: true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>只补带 tool_calls 的助手消息：给普通消息塞空字段是白改请求体，还打乱前缀缓存</summary>
    [Fact]
    public void FillEmptyReasoning_PlainMessage_IsUntouched()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, null, false, FillEmptyReasoning: true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>模型不要求时保持原样：连改写都不做，照发 SDK 那份</summary>
    [Fact]
    public void FillEmptyReasoning_Off_LeavesFieldAbsent()
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, null, false));

        Assert.Null(rewritten);
    }

    /// <summary>预设表接线：DeepSeek 系两条都开，商汤自研只回填正文、不补空串</summary>
    [Fact]
    public void RequiresReasoningContentOnAssistantMessages_FollowsPreset()
    {
        var deepseek = new RemoteModelInfo { Config = new RemoteDeepSeekModelConfig { ModelId = "deepseek-v4-flash" } };
        var sensenovaDeepseek = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "deepseek-v4-flash" },
        };
        var sensenovaOwn = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" },
        };
        var generic = new RemoteModelInfo { Config = new RemoteModelConfig { ModelId = "anything" } };

        Assert.True(OpenAICompatibleRequestRewriter.For(deepseek).FillEmptyReasoning);
        Assert.True(OpenAICompatibleRequestRewriter.For(sensenovaDeepseek).FillEmptyReasoning);
        Assert.False(OpenAICompatibleRequestRewriter.For(sensenovaOwn).FillEmptyReasoning);
        Assert.False(OpenAICompatibleRequestRewriter.For(generic).FillEmptyReasoning);
    }

    /// <summary>
    /// 补不补看<b>本次请求是否可能处于思考模式</b>：DeepSeek 的硬校验是「思考模式下带 tool_calls
    /// 的助手消息必须含思考字段」，正文调用（None 档显式 thinking:disabled）不要求。
    /// Default 不干预、由模型自己决定是否思考，仍按「可能思考」保守补上。
    /// </summary>
    [Theory]
    [InlineData(EThinkingMode.Default, true)]
    [InlineData(EThinkingMode.None, false)]
    [InlineData(EThinkingMode.Light, true)]
    [InlineData(EThinkingMode.Medium, true)]
    [InlineData(EThinkingMode.High, true)]
    [InlineData(EThinkingMode.Max, true)]
    public void FillEmptyReasoning_FollowsThinkingMode(EThinkingMode mode, bool expected)
    {
        var deepseek = new RemoteModelInfo
        {
            Config = new RemoteDeepSeekModelConfig
            {
                ModelId = "deepseek-v4-flash",
                ThinkingMode = mode,
            },
        };

        Assert.Equal(expected, OpenAICompatibleRequestRewriter.For(deepseek).FillEmptyReasoning);
    }

    private static JsonObject RestoreFirstMessage(string json, Dictionary<string, string> reasoning,
        string reasoningField = "reasoning_content")
    {
        string? rewritten = OpenAICompatibleRequestRewriterTests.Rewrite(json,
            new RequestRewrite(null, false, reasoning, false, ReasoningField: reasoningField));
        return (JsonObject)JsonNode.Parse(rewritten ?? json)!["messages"]![0]!;
    }
}
