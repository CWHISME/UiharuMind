using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 一次思考请求：开还是关；开的时候可以带档位值。
/// 只有开关的模型（<c>toggle</c>）没有档位，<see cref="Level"/> 给 <c>null</c>。
/// </summary>
/// <param name="Enabled">是否开启思考</param>
/// <param name="Level">档位值；<c>null</c> 表示该模型没有档位可选</param>
public readonly record struct ThinkingSwitch(bool Enabled, string? Level);

/// <summary>
/// 把一次思考请求翻译成请求体字段。字段名按 <see cref="EThinkingFormat"/> 分派——
/// 没有统一的字段名，只有统一的语义（开/关/档位）。
///
/// 「不干预」不在这里：那种情况一个思考字段都不该发，由调用方直接只给 <c>max_tokens</c>。
/// </summary>
public static class ThinkingParamWriter
{
    /// <summary>
    /// 产出要写入请求 JSON 的键值对
    /// </summary>
    /// <param name="format">该供应商的思考字段写法</param>
    /// <param name="thinking">开关与档位</param>
    /// <param name="maxTokens">输出预算</param>
    /// <returns>含 <c>max_tokens</c> 的参数列表</returns>
    public static IReadOnlyList<KeyValuePair<string, JsonNode?>> Write(
        EThinkingFormat format, ThinkingSwitch thinking, int maxTokens) => format switch
    {
        EThinkingFormat.ChatTemplate => ChatTemplate(thinking, maxTokens),
        EThinkingFormat.ThinkingType => ThinkingType(thinking, maxTokens),
        _ => Effort(thinking, maxTokens),
    };

    /// <summary>OpenAI 兼容：只有 <c>reasoning_effort</c>，关闭写 <c>none</c></summary>
    private static IReadOnlyList<KeyValuePair<string, JsonNode?>> Effort(ThinkingSwitch thinking, int maxTokens)
    {
        // 只有开关的模型在 OpenAI 兼容端点上传不了「开」——没有档位就没有字段可写，
        // 只能靠不发字段走模型默认（这类模型默认本来就是思考的）
        if (thinking.Enabled && thinking.Level is null) return [MaxTokens(maxTokens)];

        return
        [
            new("reasoning_effort", JsonValue.Create(thinking.Enabled ? thinking.Level : ThinkingEfforts.Off)),
            MaxTokens(maxTokens),
        ];
    }

    /// <summary>智谱 / DeepSeek：<c>thinking.type</c> 开关，有档位时再附 <c>reasoning_effort</c></summary>
    private static IReadOnlyList<KeyValuePair<string, JsonNode?>> ThinkingType(ThinkingSwitch thinking, int maxTokens)
    {
        List<KeyValuePair<string, JsonNode?>> parameters =
        [
            new("thinking", new JsonObject { ["type"] = thinking.Enabled ? "enabled" : "disabled" }),
        ];
        if (thinking.Enabled && thinking.Level is { Length: > 0 } level)
        {
            parameters.Add(new("reasoning_effort", JsonValue.Create(level)));
        }

        parameters.Add(MaxTokens(maxTokens));
        return parameters;
    }

    /// <summary>Agnes：只有 <c>chat_template_kwargs.enable_thinking</c> 布尔，没有力度分级</summary>
    private static IReadOnlyList<KeyValuePair<string, JsonNode?>> ChatTemplate(ThinkingSwitch thinking, int maxTokens) =>
    [
        new("chat_template_kwargs", new JsonObject { ["enable_thinking"] = JsonValue.Create(thinking.Enabled) }),
        MaxTokens(maxTokens),
    ];

    private static KeyValuePair<string, JsonNode?> MaxTokens(int maxTokens) =>
        new("max_tokens", JsonValue.Create(maxTokens));
}
