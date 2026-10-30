using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.AI.Net.Transport;
using UiharuMind.Core.AI.Net.Request;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 请求改写：采样参数剥离（Kimi 类固定参数模型）与思考正文回填。
///
/// 回填按<b>助手消息出现顺序</b>匹配，不看 tool_call id——没有工具调用的普通回复同样要回传
/// （网关按「每一条更早的助手消息」校验，缺了报
/// <c>Missing required parameter: 'messages[8].reasoning_content'</c>）。
/// </summary>
public class OpenAICompatibleReasoningRoundtripTests
{
    [Fact]
    public void RestoreReasoningContent_FillsToolCallMessage()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            ["先想好再调工具。"]);

        Assert.Equal("先想好再调工具。", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// 回归：网关口径下纯文本的助手回复也要回填。此前只认带 tool_calls 的消息，网关校验
    /// 「每一条助手消息」时就死在这一条上（实测 CommandCode 的 messages[8]）。
    /// </summary>
    [Fact]
    public void RestoreReasoningContent_FillsPlainAssistantMessage()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","content":"你好呀"}]}""",
            ["先打个招呼。"],
            fill: true);

        Assert.Equal("先打个招呼。", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>
    /// 有正文就发，不挑消息：工具调用之外的纯文本回复也带上。各家校验口径不同
    /// （DeepSeek 只查带 tool_calls 的，网关连纯文本回复都查），全都带回去就同时满足，
    /// 不用猜这一家要哪一种。
    /// </summary>
    [Fact]
    public void RestoreReasoningContent_FillsPlainMessageWithoutAnyFlag()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","content":"你好呀"}]}""",
            ["先打个招呼。"]);

        Assert.Equal("先打个招呼。", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>按序取：三条助手消息各拿各的思考，不串台、不错位</summary>
    [Fact]
    public void RestoreReasoningContent_FollowsAssistantOrder()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """
            {"messages":[
              {"role":"system","content":"s"},
              {"role":"user","content":"u"},
              {"role":"assistant","tool_calls":[{"id":"c1"}]},
              {"role":"tool","content":"r"},
              {"role":"assistant","content":"看完了"},
              {"role":"user","content":"u2"},
              {"role":"assistant","content":"好"}
            ]}
            """,
            new RequestRewrite(null, false, ["第一段", null, "第三段"], false));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.Null(messages[0]!["reasoning_content"]);
        Assert.Null(messages[1]!["reasoning_content"]);
        Assert.Equal("第一段", messages[2]!["reasoning_content"]!.GetValue<string>());
        Assert.Null(messages[3]!["reasoning_content"]);
        Assert.Null(messages[4]!["reasoning_content"]); // 该条历史里没有思考，不补（fill=None）
        Assert.Null(messages[5]!["reasoning_content"]);
        Assert.Equal("第三段", messages[6]!["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>非助手消息一律不碰</summary>
    [Fact]
    public void RestoreReasoningContent_SkipsNonAssistantMessages()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"user","content":"你好"}]}""",
            ["用不上的思考。"]);

        Assert.Null(message["reasoning_content"]);
    }

    [Fact]
    public void RestoreReasoningContent_KeepsExistingContent()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","reasoning_content":"已有的思考。","tool_calls":[{"id":"call-1"}]}]}""",
            ["新的思考，不该覆盖。"]);

        Assert.Equal("已有的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_ReplacesExplicitNull()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","reasoning_content":null,"tool_calls":[{"id":"call-1"}]}]}""",
            ["补上的思考。"]);

        Assert.Equal("补上的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>回填键名跟随模型（如 reasoning），不写死 reasoning_content</summary>
    [Fact]
    public void RestoreReasoningContent_UsesConfiguredFieldName()
    {
        JsonObject message = RestoreMessage(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            ["先想好再调工具。"],
            reasoningField: "reasoning");

        Assert.Equal("先想好再调工具。", message["reasoning"]!.GetValue<string>());
        Assert.Null(message["reasoning_content"]);
    }

    /// <summary>roundtrip=null(自动)与 true 都去收集(收集器扫不到就空);显式 false 与无模型跳过</summary>
    [Fact]
    public void ReasoningSource_IsCollectedForAutoOrRequired_SkippedForExplicitOff()
    {
        int count = 0;
        // 上下文直接给：断言「什么情况下要收集」不必先摆一个异步流，也不必担心漏到别的测试里
        LlmRequestContext context = new()
        {
            PendingReasoningSource = () =>
            {
                count++;
                return ["思考"];
            },
        };
        // 显式关(商汤自研,官方推荐不回传):不收集
        RequestRewrite.For(new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" } }, context);
        // 无模型:不收集
        RequestRewrite.For(null, context);
        // 自动(未声明):收集
        RequestRewrite.For(new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } }, context);
        // 必须回填(kimi-k3):收集
        RequestRewrite rewrite = RequestRewrite.For(
            new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "kimi-k3" } }, context);
        Assert.NotNull(rewrite.ReasoningByAssistantMessage);

        Assert.Equal(2, count);
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
    /// 从模型配置一路查到请求体里的键名：预设表 → 默认，<b>观测值不参与</b>。
    /// 观测到的是响应侧字段名，请求侧未必同名（CommandCode 响应 reasoning、请求 reasoning_content）。
    /// </summary>
    [Fact]
    public void ReasoningField_ResolvesPresetThenDefault_IgnoringObserved()
    {
        var observed = new RemoteModelInfo { Config = new RemoteSensenovaModelConfig { ModelId = "glm-5.2" } };
        observed.Config.ObservedThinkingFields["glm-5.2"] = "reasoning_text";
        Assert.Equal("reasoning_content", RequestRewrite.For(observed).ReasoningField);

        // 预设声明照常生效
        var preseted = new RemoteModelInfo
        {
            Config = new RemoteSensenovaModelConfig { ModelId = "sensenova-6.8-flash-lite" },
        };
        Assert.Equal("reasoning", RequestRewrite.For(preseted).ReasoningField);

        Assert.Equal("reasoning_content", RequestRewrite.For(new RemoteModelInfo()).ReasoningField);
        Assert.Equal("reasoning_content", RequestRewrite.For(model: null).ReasoningField);
        // 违反契约给了空串的实现：退回默认，而不是写一个 `"": "..."` 进请求体
        Assert.Equal("reasoning_content", RequestRewrite.For(new BlankFieldModel()).ReasoningField);
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
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, null, false, true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>要求每条都带时，纯文本回复没正文也补空串——字段缺了就是 400</summary>
    [Fact]
    public void FillEmptyReasoning_PlainMessageGetsEmptyString()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, null, false, true));

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
            new RequestRewrite(null, false, null, false, true));

        JsonArray messages = (JsonArray)JsonNode.Parse(rewritten!)!["messages"]!;
        Assert.Null(messages[0]!["reasoning_content"]); // user 不补
        Assert.Equal("", messages[1]!["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>有正文就回正文，不因「补空串」把它盖成空串</summary>
    [Fact]
    public void FillEmptyReasoning_PrefersExistingReasoning()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"说完了"}]}""",
            new RequestRewrite(null, false, ["真的思考"], false, true));

        var message = (JsonObject)JsonNode.Parse(rewritten!)!["messages"]![0]!;
        Assert.Equal("真的思考", message["reasoning_content"]!.GetValue<string>());
    }

    /// <summary>模型不要求时保持原样：连改写都不做，照发 SDK 那份</summary>
    [Fact]
    public void FillEmptyReasoning_Off_LeavesFieldAbsent()
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(
            """{"messages":[{"role":"assistant","content":"查一下","tool_calls":[{"id":"call-1","type":"function"}]}]}""",
            new RequestRewrite(null, false, null, false));

        Assert.Null(rewritten);
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
    /// 所以这里不给任何供应商写死，靠服务端 400 里学（见 <see cref="ReasoningRequirementErrorHandler"/>）。
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

    /// <summary>
    /// 收集器按助手消息顺序取，普通回复也收：此前只收带 tool_calls 的消息，
    /// 纯文本回复的思考整段丢掉（CommandCode 的 messages[8] 就是这么 400 的）。
    /// </summary>
    [Fact]
    public void CollectAssistantReasoning_CoversPlainRepliesInOrder()
    {
        ChatMessage[] history =
        [
            new(ChatRole.User, "你好"),
            new(ChatRole.Assistant, [new TextReasoningContent("先想"), new FunctionCallContent("c1", "Read", null)]),
            new(ChatRole.Tool, [new FunctionResultContent("c1", "内容")]),
            new(ChatRole.Assistant, "看完了"),
            new(ChatRole.Assistant, [new TextReasoningContent("收尾")]),
        ];

        IReadOnlyList<string?>? collected = LazyChatClient.CollectAssistantReasoning(history);

        Assert.NotNull(collected);
        Assert.Equal(3, collected.Count); // 只数助手消息
        Assert.Equal("先想", collected[0]);
        Assert.Null(collected[1]);
        Assert.Equal("收尾", collected[2]);
    }

    /// <summary>老会话里一条消息躺着多段思考碎片，要拼齐</summary>
    [Fact]
    public void CollectAssistantReasoning_JoinsFragments()
    {
        ChatMessage[] history =
        [
            new(ChatRole.Assistant, [new TextReasoningContent("先想"), new TextReasoningContent("再想")]),
        ];

        IReadOnlyList<string?>? collected = LazyChatClient.CollectAssistantReasoning(history);

        Assert.Equal("先想再想", collected![0]);
    }

    /// <summary>一条思考都没有时返回 null，改写整段跳过</summary>
    [Fact]
    public void CollectAssistantReasoning_ReturnsNullWhenNothingToReplay()
    {
        ChatMessage[] history = [new(ChatRole.User, "你好"), new(ChatRole.Assistant, "答")];

        Assert.Null(LazyChatClient.CollectAssistantReasoning(history));
    }

    private static JsonObject RestoreMessage(string json, string?[] reasoning,
        string reasoningField = "reasoning_content", bool fill = false)
    {
        string? rewritten = ChatCompletionsRequestRewriterTests.Rewrite(json,
            new RequestRewrite(null, false, reasoning, false, fill, reasoningField));
        return (JsonObject)JsonNode.Parse(rewritten ?? json)!["messages"]![0]!;
    }
}
