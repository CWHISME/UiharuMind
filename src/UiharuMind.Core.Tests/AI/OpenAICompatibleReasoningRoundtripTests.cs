using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
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
    public void StripSamplingParams_RemovesOnlySamplingKeys()
    {
        var json = new JsonObject
        {
            ["model"] = "kimi-k3",
            ["temperature"] = 0.7,
            ["top_p"] = 0.9,
            ["presence_penalty"] = 0.5,
            ["frequency_penalty"] = 0.5,
            ["max_tokens"] = 131072,
            ["thinking"] = new JsonObject { ["type"] = "enabled" },
        };

        OpenAICompatibleHttpHandler.StripSamplingParams(json);

        Assert.Null(json["temperature"]);
        Assert.Null(json["top_p"]);
        Assert.Null(json["presence_penalty"]);
        Assert.Null(json["frequency_penalty"]);
        Assert.Equal(131072, json["max_tokens"]!.GetValue<int>());
        Assert.NotNull(json["thinking"]);
    }

    [Fact]
    public void RestoreReasoningContent_FillsMissingByCallId()
    {
        var json = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["content"] = "查一下",
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject { ["id"] = "call-1", ["type"] = "function" },
                    },
                },
            },
        };
        var reasoning = new Dictionary<string, string> { ["call-1"] = "先想好再调工具。" };

        OpenAICompatibleHttpHandler.RestoreReasoningContent(json, reasoning);

        var message = (JsonObject)json["messages"]![0]!;
        Assert.Equal("先想好再调工具。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_MatchesAnyCallId()
    {
        var json = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject { ["id"] = "call-unknown" },
                        new JsonObject { ["id"] = "call-2" },
                    },
                },
            },
        };
        var reasoning = new Dictionary<string, string> { ["call-2"] = "第二个调用的思考。" };

        OpenAICompatibleHttpHandler.RestoreReasoningContent(json, reasoning);

        var message = (JsonObject)json["messages"]![0]!;
        Assert.Equal("第二个调用的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_KeepsExistingContent()
    {
        var json = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject
                {
                    ["role"] = "assistant",
                    ["reasoning_content"] = "已有的思考。",
                    ["tool_calls"] = new JsonArray
                    {
                        new JsonObject { ["id"] = "call-1" },
                    },
                },
            },
        };
        var reasoning = new Dictionary<string, string> { ["call-1"] = "新的思考，不该覆盖。" };

        OpenAICompatibleHttpHandler.RestoreReasoningContent(json, reasoning);

        var message = (JsonObject)json["messages"]![0]!;
        Assert.Equal("已有的思考。", message["reasoning_content"]!.GetValue<string>());
    }

    [Fact]
    public void RestoreReasoningContent_SkipsMessagesWithoutToolCalls()
    {
        var json = new JsonObject
        {
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "user", ["content"] = "你好" },
            },
        };
        var reasoning = new Dictionary<string, string> { ["call-1"] = "用不上的思考。" };

        OpenAICompatibleHttpHandler.RestoreReasoningContent(json, reasoning);

        var message = (JsonObject)json["messages"]![0]!;
        Assert.Null(message["reasoning_content"]);
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
}
