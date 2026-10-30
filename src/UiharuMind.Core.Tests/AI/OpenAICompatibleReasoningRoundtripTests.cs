using System.Text.Json.Nodes;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Request;
using UiharuMind.Core.AI.Reasoning;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 请求改写里与思考有关的那半条：「字段必须在」的模型补空串，以及键名从哪来。
///
/// <para>回填正文与票据<b>不在这层</b>（ADR 0101）：那两样跟着消息对象走，
/// 由 <see cref="ChatCompletionMessageBinder"/> 写进消息本身，见
/// <see cref="ChatCompletionMessageBinderTests"/>。这里只剩「补空串」——
/// 它不需要知道是哪条消息，天然不会错配。</para>
/// </summary>
public class OpenAICompatibleReasoningRoundtripTests
{
    /// <summary>
    /// 没有思考正文可回传时补空串：DeepSeek 类要求「思考模式下带 tool_calls 的助手消息必须含
    /// reasoning_content」，字段缺了直接 400——历史里关思考档位跑出来的工具调用就是这种。
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_NoThinkingOnToolCallMessage_WritesEmptyString()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, false, true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>要求每条都带时，纯文本回复没正文也补空串——字段缺了就是 400</summary>
    [Fact]
    public void FillEmptyReasoning_PlainMessageGetsEmptyString()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, false, true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// 每一条助手消息都要带字段：历史里没有思考的那些（关思考档位跑出来的旧回复）补空串，
    /// 非助手消息一律不碰。
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_OnlyTouchesAssistantMessages()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"user","content":"你好"},{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, false, true));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.Null(messages[0]!["reasoning_content"]); // user 不补
        Assert.Equal("", messages[1]!["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// 已经有正文的不覆盖。绑定器写进去的那些就是这种形态——补空串要是盖上去，
    /// 等于把思考抹成空串，服务端照样 400（字段在、内容是空，语义不同）。
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_PrefersExistingReasoning()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","reasoning_content":"真的思考","content":"说完了"}]}""",
            new RequestRewrite(null, false, false, true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("真的思考", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>模型不要求时保持原样：连改写都不做，照发 SDK 那份</summary>
    [Fact]
    public void FillEmptyReasoning_Off_LeavesFieldAbsent()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, false));

        Assert.Null(rewritten);
    }

    /// <summary>补空串用模型声明的键名（如 reasoning），不写死 reasoning_content</summary>
    [Fact]
    public void FillEmptyReasoning_UsesConfiguredFieldName()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, false, true, "reasoning"));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("", message["reasoning"]!.GetValue<string>());
        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>思考键为 null 时补空串只换值，不重复写键</summary>
    [Fact]
    public void FillEmptyReasoning_ReplacesExplicitNull()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","reasoning_content":null,"tool_calls":[{"id":"c1","function":{"arguments":"null"}}]}]}""",
            new RequestRewrite(null, false, false, true));

        Assert.Equal(
            """{"messages":[{"role":"assistant","reasoning_content":"","tool_calls":[{"id":"c1","function":{"arguments":"{}"}}]}]}""",
            rewritten);
    }

    /// <summary>
    /// 口径来源：DeepSeek 系（含转售、含模型 ID 兜底）一律要求每条都带——与 opencode 的
    /// <c>requireReasoning</c> 默认判定同款；商汤自研与通用模型不要求。
    /// </summary>
    [Fact]
    public void FillEmptyReasoning_FollowsModelFamily()
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

        Assert.True(RequestRewrite.For(deepseek).FillEmptyReasoning);
        Assert.True(RequestRewrite.For(sensenovaDeepseek).FillEmptyReasoning);
        Assert.False(RequestRewrite.For(sensenovaOwn).FillEmptyReasoning);
        Assert.False(RequestRewrite.For(generic).FillEmptyReasoning);
    }

    /// <summary>转售的 DeepSeek 不在预设表里（OpenCode），按模型 ID 兜底开全量口径</summary>
    [Fact]
    public void FillEmptyReasoning_CoversResoldDeepSeekByModelId()
    {
        var resold = new RemoteModelInfo
        {
            Config = new RemoteOpenCodeModelConfig { ModelId = "deepseek/deepseek-v4.1-flash" },
        };
        var other = new RemoteModelInfo
        {
            Config = new RemoteOpenCodeModelConfig { ModelId = "kimi-k2.5" },
        };

        Assert.True(RequestRewrite.For(resold).FillEmptyReasoning);
        Assert.False(RequestRewrite.For(other).FillEmptyReasoning);
    }

    /// <summary>
    /// 「每条助手消息都带思考」是按<b>模型</b>的，不是按网关：同一网关下 DeepSeek 只要求带
    /// tool_calls 的那些（实测 88 次请求、105 条无思考的纯文本回复全部通过），beam 才要求全量。
    /// 所以这里不给任何供应商写死，靠服务端 400 里学（见 <c>ReasoningRequirementErrorHandler</c>）。
    /// </summary>
    [Fact]
    public void RequireReasoningOnEveryAssistantMessage_IsLearnedPerModel()
    {
        var gateway = new RemoteModelInfo
        {
            Config = new RemoteCommandCodeModelConfig { ModelId = "reflection/beam:free" },
        };
        Assert.False(gateway.RequireReasoningOnEveryAssistantMessage);
        Assert.False(RequestRewrite.For(gateway).FillEmptyReasoning);

        // 服务端报了一次 → 记下这个 ModelId → 改写立刻改用全量口径
        Assert.True(gateway.Config.LearnRequireReasoningOnEveryAssistantMessage());
        Assert.True(gateway.RequireReasoningOnEveryAssistantMessage);
        Assert.True(RequestRewrite.For(gateway).FillEmptyReasoning);

        // 同端点另一个模型不受这次学习影响（它是靠 deepseek 兜底开的，不是靠学）
        gateway.Config.ModelId = "deepseek/deepseek-v4.1-flash";
        Assert.True(gateway.RequireReasoningOnEveryAssistantMessage);
        Assert.Empty(gateway.Config.ReasoningEveryAssistantModelIds.Where(x => x.Contains("deepseek")));
        gateway.Config.ModelId = "reflection/beam:free";
        Assert.False(gateway.Config.LearnRequireReasoningOnEveryAssistantMessage());
    }

    /// <summary>学到「每条都要带」的模型不受思考档位影响：None 档照样补</summary>
    [Fact]
    public void FillEmptyReasoning_LearnedModelIgnoresThinkingMode()
    {
        var config = new RemoteCommandCodeModelConfig
        {
            ModelId = "reflection/beam:free",
            ThinkingEffort = ThinkingEfforts.Off,
        };
        config.LearnRequireReasoningOnEveryAssistantMessage();

        Assert.True(RequestRewrite.For(new RemoteModelInfo { Config = config }).FillEmptyReasoning);
    }

    [Fact]
    public void KimiK3Preset_OmitsSamplingParams_RoundtripIsAuto()
    {
        var config = new RemoteSensenovaModelConfig();
        var preset = config.ModelIdVariants["kimi-k3"];

        Assert.True(preset.OmitSamplingParams);
        Assert.Null(preset.ReplayExistingReasoning); // 默认 auto：有正文就回传
    }

    [Fact]
    public void KimiK3ModelInfo_RoundtripDefaultsToAuto()
    {
        var info = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3" },
        };

        Assert.Null(info.ReplayExistingReasoning);
    }

    /// <summary>
    /// 回填开关：<c>null</c>（自动）与 <c>true</c> 都回填，只有显式 <c>false</c> 与无模型才跳过。
    /// 绑定器与改写器问的是同一个谓词，抄成两份就会自相矛盾。
    /// </summary>
    [Fact]
    public void ReplayPolicy_ReplaysForAutoOrRequired_SkipsForExplicitOff()
    {
        // 显式关(商汤自研,官方推荐不回传)
        Assert.False(ReasoningReplayPolicy.ShouldReplay(
            new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } }));
        // 无模型
        Assert.False(ReasoningReplayPolicy.ShouldReplay(null));
        // 自动(未声明)
        Assert.True(ReasoningReplayPolicy.ShouldReplay(
            new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } }));
        // 必须回填(kimi-k3 之外由服务端 400 学来的那种)
        var learned = new RemoteModelInfo { Config = new RemoteCommandCodeModelConfig { ModelId = "beam" } };
        learned.Config.LearnRequireReasoningOnEveryAssistantMessage();
        Assert.True(ReasoningReplayPolicy.ShouldReplay(learned));
    }

    /// <summary>
    /// 从模型配置一路查到请求体里的键名：预设表 → 默认，<b>观测值不参与</b>。
    /// 观测到的是响应侧字段名，请求侧未必同名（CommandCode 响应 reasoning、请求 reasoning_content）。
    /// </summary>
    [Fact]
    public void ReasoningField_ResolvesPresetThenDefault_IgnoringObserved()
    {
        var observed = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } };
        observed.Config.ObservedThinkingFields["glm-5.2"] = "reasoning_text";
        Assert.Equal("reasoning_content", RequestRewrite.For(observed).ReasoningField);
        Assert.Equal("reasoning_content", ReasoningReplayPolicy.FieldName(observed));

        // 预设声明照常生效
        var preseted = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" },
        };
        Assert.Equal("reasoning", RequestRewrite.For(preseted).ReasoningField);
        Assert.Equal("reasoning", ReasoningReplayPolicy.FieldName(preseted));

        Assert.Equal("reasoning_content", ReasoningReplayPolicy.FieldName(new RemoteModelInfo()));
        Assert.Equal("reasoning_content", ReasoningReplayPolicy.FieldName(null));
        // 违反契约给了空串的实现：退回默认，而不是写一个 `"": "..."` 进请求体
        Assert.Equal("reasoning_content", ReasoningReplayPolicy.FieldName(new BlankFieldModel()));
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
}
