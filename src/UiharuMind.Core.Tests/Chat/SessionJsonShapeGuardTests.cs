using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 会话存档 JSON 的验收护栏（主题二 A1 source-gen 改造用，ADR 0029 前置）。
/// 普拉娜/塔奇克马/用户（化身）拍板的两层验收：
///   第一层：旧存档进 → 新代码序列化 → 逐字节比（保形状）；
///   第二层：旧存档进 → 新代码反序列化 → 值类型语义（保语义）。
/// 改造（SessionJsonOptions 挂 JsonSerializerContext）后这两层必须同时绿；
/// 任何一层红 = 引入了静默形状/语义漂移，不得合入。
/// 黄金字节来自改造前的反射模式输出（探针 $DRAFT/aot-probe 固化）。
/// </summary>
public class SessionJsonShapeGuardTests
{
    /// <summary>代表性消息集：覆盖项目真实使用面（Text/Reasoning/Data/FunctionCall/FunctionResult）</summary>
    private static readonly List<ChatMessage> Representative = new()
    {
        new ChatMessage(ChatRole.User, "你好，老师"),
        new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new TextReasoningContent("让我想想"),
            new TextContent("答案是 42"),
        }),
        new ChatMessage(ChatRole.User, new List<AIContent>
        {
            new DataContent(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "image/png"),
            new TextContent("看看这张图"),
        }),
        new ChatMessage(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call_001", "sendMessage", new Dictionary<string, object?>
            {
                ["text"] = "hello",
                ["count"] = 3,
                ["urgent"] = true,
                ["score"] = 1.5,
                ["tags"] = new[] { "a", "b" },
                ["nested"] = new Dictionary<string, object?> { ["x"] = 1 },
                ["nothing"] = null,
            }),
            new FunctionCallContent("call_002", "noArgs"),
        }),
        new ChatMessage(ChatRole.Tool, new List<AIContent>
        {
            new FunctionResultContent("call_001", "ok"),
        }),
    };

    /// <summary>第一层：形状。每条代表消息的序列化输出必须与改造前逐字节一致。</summary>
    [Fact]
    public void Serialize_MatchesGoldenBytes_ForRepresentativeMessages()
    {
        string[] golden = [Golden.UserText, Golden.AssistantReasoningAndText, Golden.UserImageAndText, Golden.AssistantFunctionCall, Golden.ToolFunctionResult];

        Assert.Equal(golden.Length, Representative.Count);
        for (int i = 0; i < Representative.Count; i++)
        {
            string actual = JsonSerializer.Serialize(Representative[i], SessionJsonOptions.Default);
            Assert.Equal(golden[i], actual);
        }
    }

    /// <summary>第二层：语义。反序列化后 $type 判别符还原、object 值类型语义保持。
    /// 注意基线：反射模式下 Arguments 里的 object 值反序列化后就是 JsonElement（探针实测），
    /// 改造后必须保持（不能变成别的类型，更不能变成 int/bool 后写回去形状漂移）。</summary>
    [Fact]
    public void Deserialize_RestoresDiscriminatorsAndValueSemantics()
    {
        string[] golden = [Golden.UserText, Golden.AssistantReasoningAndText, Golden.UserImageAndText, Golden.AssistantFunctionCall, Golden.ToolFunctionResult];

        List<ChatMessage> restored = golden.Select(g => JsonSerializer.Deserialize<ChatMessage>(g, SessionJsonOptions.Default)!).ToList();

        // [0] 纯文本
        Assert.Equal(ChatRole.User, restored[0].Role);
        Assert.Single(restored[0].Contents);
        TextContent t0 = Assert.IsType<TextContent>(restored[0].Contents[0]);
        Assert.Equal("你好，老师", t0.Text);

        // [1] 思考 + 文本：$type 判别符必须还原成具体类型
        Assert.Equal(ChatRole.Assistant, restored[1].Role);
        Assert.Equal(2, restored[1].Contents.Count);
        TextReasoningContent r1 = Assert.IsType<TextReasoningContent>(restored[1].Contents[0]);
        Assert.Equal("让我想想", r1.Text);
        Assert.Equal("答案是 42", Assert.IsType<TextContent>(restored[1].Contents[1]).Text);

        // [2] 图片 + 文本
        DataContent d2 = Assert.IsType<DataContent>(restored[2].Contents[0]);
        Assert.Equal("image/png", d2.MediaType);
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", d2.Uri!.ToString());
        Assert.Equal("看看这张图", Assert.IsType<TextContent>(restored[2].Contents[1]).Text);

        // [3] 工具调用：object 参数值一律还原成 JsonElement（与反射模式基线一致）
        Assert.Equal(2, restored[3].Contents.Count);
        FunctionCallContent call0 = Assert.IsType<FunctionCallContent>(restored[3].Contents[0]);
        Assert.Equal("sendMessage", call0.Name);
        Assert.Equal("call_001", call0.CallId);
        Assert.NotNull(call0.Arguments);
        AssertJsonArgument(call0.Arguments!, "text", "hello");
        AssertJsonArgument(call0.Arguments!, "count", "3");
        AssertJsonArgument(call0.Arguments!, "urgent", "True");
        AssertJsonArgument(call0.Arguments!, "score", "1.5");
        AssertJsonArgument(call0.Arguments!, "nothing", null);
        Assert.Equal(JsonValueKind.Array, JsonElementThrow(call0.Arguments!["tags"]).ValueKind);
        Assert.Equal(JsonValueKind.Object, JsonElementThrow(call0.Arguments!["nested"]).ValueKind);
        FunctionCallContent call1 = Assert.IsType<FunctionCallContent>(restored[3].Contents[1]);
        Assert.Equal("noArgs", call1.Name);
        Assert.Equal("call_002", call1.CallId);

        // [4] 工具结果
        FunctionResultContent fr = Assert.IsType<FunctionResultContent>(restored[4].Contents[0]);
        Assert.Equal("call_001", fr.CallId);
        Assert.NotNull(fr.Result);
        Assert.Equal("ok", fr.Result!.ToString());
    }

    /// <summary>用户审出的洞（带类型字典掉 IEnumerable 分支）锁死用例 + AdditionalProperties 复杂对象往返：形状与语义各一组。</summary>
    [Fact]
    public void Serialize_MatchesGoldenBytes_WithTypedDictionaryInArguments()
    {
        ChatMessage message = new(ChatRole.Assistant, new List<AIContent>
        {
            new FunctionCallContent("call_003", "lookup", new Dictionary<string, object?>
            {
                ["map"] = new Dictionary<string, string> { ["a"] = "x", ["b"] = "y" },
            }),
        });

        Assert.Equal(Golden.TypedDictionaryInArguments, JsonSerializer.Serialize(message, SessionJsonOptions.Default));

        ChatMessage restored = JsonSerializer.Deserialize<ChatMessage>(Golden.TypedDictionaryInArguments, SessionJsonOptions.Default)!;
        FunctionCallContent call = Assert.IsType<FunctionCallContent>(restored.Contents[0]);
        Assert.True(call.Arguments!.TryGetValue("map", out object? map));
        JsonElement mapElement = Assert.IsType<JsonElement>(map);
        Assert.Equal(JsonValueKind.Object, mapElement.ValueKind);
        Assert.Equal("x", mapElement.GetProperty("a").GetString());
        Assert.Equal("y", mapElement.GetProperty("b").GetString());
    }

    /// <summary>AdditionalProperties 里的复杂对象（AgentRequestMessageSourceAttribution）必须按对象写、往返还原（反射基线）。</summary>
    [Fact]
    public void Serialize_MatchesGoldenBytes_WithAttributionInAdditionalProperties()
    {
        ChatMessage message = new ChatMessage(ChatRole.User, "hi")
            .WithAgentRequestMessageSource(AgentRequestMessageSourceType.ChatHistory, "source");

        Assert.Equal(Golden.AttributionInAdditionalProperties, JsonSerializer.Serialize(message, SessionJsonOptions.Default));

        ChatMessage restored = JsonSerializer.Deserialize<ChatMessage>(Golden.AttributionInAdditionalProperties, SessionJsonOptions.Default)!;
        Assert.True(restored.AdditionalProperties!.TryGetValue("_attribution", out object? attribution));
        JsonElement attr = Assert.IsType<JsonElement>(attribution);
        Assert.Equal("ChatHistory", attr.GetProperty("sourceType").GetProperty("value").GetString());
        Assert.Equal("source", attr.GetProperty("sourceId").GetString());
    }

    private static void AssertJsonArgument(IDictionary<string, object?> args, string key, string? toEqual)
    {
        Assert.True(args.TryGetValue(key, out object? value), $"missing argument {key}");
        if (toEqual is null)
        {
            Assert.Null(value);
            return;
        }
        Assert.IsType<JsonElement>(value);
        Assert.Equal(toEqual, value!.ToString());
    }

    private static JsonElement JsonElementThrow(object? value)
    {
        Assert.IsType<JsonElement>(value);
        return (JsonElement)value!;
    }

    /// <summary>改造前反射模式输出的黄金字节（探针固化，勿手改）</summary>
    private static class Golden
    {
        public const string UserText = """
{
  "role": "user",
  "contents": [
    {
      "$type": "text",
      "text": "你好，老师"
    }
  ]
}
""";

        public const string AssistantReasoningAndText = """
{
  "role": "assistant",
  "contents": [
    {
      "$type": "reasoning",
      "text": "让我想想"
    },
    {
      "$type": "text",
      "text": "答案是 42"
    }
  ]
}
""";

        public const string UserImageAndText = """
{
  "role": "user",
  "contents": [
    {
      "$type": "data",
      "uri": "data:image/png;base64,iVBORw0KGgo="
    },
    {
      "$type": "text",
      "text": "看看这张图"
    }
  ]
}
""";

        public const string AssistantFunctionCall = """
{
  "role": "assistant",
  "contents": [
    {
      "$type": "functionCall",
      "name": "sendMessage",
      "arguments": {
        "text": "hello",
        "count": 3,
        "urgent": true,
        "score": 1.5,
        "tags": [
          "a",
          "b"
        ],
        "nested": {
          "x": 1
        },
        "nothing": null
      },
      "informationalOnly": false,
      "callId": "call_001"
    },
    {
      "$type": "functionCall",
      "name": "noArgs",
      "informationalOnly": false,
      "callId": "call_002"
    }
  ]
}
""";

        public const string ToolFunctionResult = """
{
  "role": "tool",
  "contents": [
    {
      "$type": "functionResult",
      "result": "ok",
      "callId": "call_001"
    }
  ]
}
""";

        /// <summary>Arguments 里带 Dictionary&lt;string,string&gt;：按对象写（用户审出洞的黄金，反射基线）</summary>
        public const string TypedDictionaryInArguments = """
{
  "role": "assistant",
  "contents": [
    {
      "$type": "functionCall",
      "name": "lookup",
      "arguments": {
        "map": {
          "a": "x",
          "b": "y"
        }
      },
      "informationalOnly": false,
      "callId": "call_003"
    }
  ]
}
""";

        /// <summary>AdditionalProperties 带 AgentRequestMessageSourceAttribution（反射基线）</summary>
        public const string AttributionInAdditionalProperties = """
{
  "role": "user",
  "contents": [
    {
      "$type": "text",
      "text": "hi"
    }
  ],
  "additionalProperties": {
    "_attribution": {
      "sourceType": {
        "value": "ChatHistory"
      },
      "sourceId": "source"
    }
  }
}
""";
    }
}