using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 归一化「OpenAI 兼容」服务的响应。
/// 部分厂商(如商汤 Sensenova)会在 chunk 里返回空的或非标准的 finish_reason，
/// 而 OpenAI SDK 的枚举解析遇到未知值会直接抛
/// "Unknown ChatFinishReason value. (Parameter 'value')"，
/// 因此在响应交给 SDK 之前先把这些值修正掉。
/// </summary>
internal static class OpenAiCompatibleResponseFixer
{
    private const string FinishReasonKey = "finish_reason";

    private static readonly HashSet<string> ValidFinishReasons = new(StringComparer.Ordinal)
    {
        "stop", "length", "content_filter", "tool_calls", "function_call"
    };

    /// <summary>
    /// 修正一行 SSE 文本
    /// </summary>
    /// <param name="line">原始行，含 data: 前缀</param>
    /// <returns>需要修正时返回新行，否则原样返回</returns>
    public static string FixEventStreamLine(string line)
    {
        const string prefix = "data:";
        if (!line.StartsWith(prefix, StringComparison.Ordinal)) return line;
        var payload = line[prefix.Length..].Trim();
        if (payload.Length == 0 || payload == "[DONE]") return line;
        var fixedPayload = FixJson(payload);
        return fixedPayload == null ? line : "data: " + fixedPayload;
    }

    /// <summary>
    /// 修正一段 chat completion 响应 JSON
    /// </summary>
    /// <param name="json">原始 JSON</param>
    /// <returns>需要修正时返回新 JSON，无需修正或解析失败返回 null</returns>
    public static string? FixJson(string json)
    {
        if (!json.Contains(FinishReasonKey, StringComparison.Ordinal)) return null;
        try
        {
            var node = JsonNode.Parse(json);
            return FixNode(node) ? node!.ToJsonString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool FixNode(JsonNode? node)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var pair in obj.ToList())
                {
                    if (pair.Key == FinishReasonKey)
                    {
                        if (TryNormalize(pair.Value, out var normalized))
                        {
                            obj[pair.Key] = normalized == null ? null : JsonValue.Create(normalized);
                            changed = true;
                        }
                    }
                    else changed |= FixNode(pair.Value);
                }

                break;
            case JsonArray array:
                foreach (var item in array) changed |= FixNode(item);
                break;
        }

        return changed;
    }

    // 空值/空串视为「本 chunk 还没结束」写回 null，非空的未知值统一当作正常结束
    private static bool TryNormalize(JsonNode? value, out string? normalized)
    {
        normalized = null;
        if (value is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var text)) return false;
        if (string.IsNullOrWhiteSpace(text)) return true;
        if (ValidFinishReasons.Contains(text)) return false;
        normalized = "stop";
        return true;
    }
}
