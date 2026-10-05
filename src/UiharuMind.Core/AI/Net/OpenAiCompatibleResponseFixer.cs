using System.Text.Json;
using System.Text.Json.Nodes;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 归一化「OpenAI 兼容」服务的响应。OpenAI SDK 的枚举解析遇到未知值一律直接抛，
/// 因此在响应交给 SDK 之前先把这些值修正掉。目前修三处，都是实际撞到过的：
/// <list type="bullet">
/// <item>空的或非标准的 <c>finish_reason</c> → "Unknown ChatFinishReason value."</item>
/// <item><c>tool_calls</c> 里空的 <c>type</c> → "Unknown ChatToolCallKind value."</item>
/// <item>商汤自研模型的思考字段 <c>reasoning</c> → <c>reasoning_content</c>（SDK 只认后者，前者会被静默丢掉，
/// 表现为「思考烧了 token 但界面一个字都没有」，见 sensenova-6.8-flash-lite 的 length 截断事故）</item>
/// </list>
///
/// 三处都在商汤 Sensenova 上实测到过。
/// </summary>
internal static class OpenAiCompatibleResponseFixer
{
    private const string FinishReasonKey = "finish_reason";
    private const string ToolCallsKey = "tool_calls";
    private const string ReasoningKey = "reasoning"; //商汤自研模型的思考字段；SDK 只认 reasoning_content
    private const string ReasoningContentKey = "reasoning_content";
    private const string TypeKey = "type";
    private const string FunctionToolCall = "function"; //OpenAI 规范里 tool_calls 的 type 只有这一个合法值
    private const string QuotedFinishReasonKey = "\"finish_reason\"";
    private const string QuotedReasoningKey = "\"reasoning\""; //带引号：不命中 reasoning_content/reasoning_tokens
    private const string QuotedReasoningContentKey = "\"reasoning_content\"";
    private const string ReasoningKeyPrefix = "\"reasoning\":"; //字面改名用：紧随 `"` 即字符串值
    private const string ReasoningContentKeyPrefix = "\"reasoning_content\":";
    private const string EmptyReasoningHead = "\"reasoning\":\"\","; //空思考在前，后随逗号
    private const string EmptyReasoningTail = ",\"reasoning\":\"\""; //空思考在后，前随逗号
    private const string NullFinishReason = "\"finish_reason\":null";
    private const string EmptyFinishReason = "\"finish_reason\":\"\"";

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
        bool hasToolCalls = json.Contains(ToolCallsKey, StringComparison.Ordinal);
        bool hasReasoning = json.Contains(QuotedReasoningKey, StringComparison.Ordinal);
        if (!json.Contains(FinishReasonKey, StringComparison.Ordinal) && !hasToolCalls && !hasReasoning) return null;

        bool literallyChanged = false;
        // 思考块的热路径：单 `"reasoning":`、无 reasoning_content、值是非空字符串时纯字面改名，不建 DOM。
        // 逐块 DOM 是流式里最大的一笔分配（83cf4ca6 才刚把它从 finish_reason 上摘掉），
        // 思考流一跑就是上万块，这里不能走回头路；拿不准的一律留给下面的 DOM 兜底
        if (hasReasoning && TryRenameReasoningKey(json, out string renamed))
        {
            json = renamed;
            literallyChanged = true;
            hasReasoning = false;
            if (!json.Contains(FinishReasonKey, StringComparison.Ordinal) && !hasToolCalls)
                return json;
        }

        // 空思考按字面删掉：思考期 chunk 里常见，不进 DOM（与 finish_reason 空串同理；
        // 值里的转义写法是 \"reasoning\":\"\" 带反斜杠，撞不上这里的字面形态）。
        // 只做删除，长度变了就是改过，后续分支返回时不能把这一笔吞掉
        if (hasReasoning)
        {
            string pruned = json.Replace(EmptyReasoningHead, string.Empty, StringComparison.Ordinal)
                .Replace(EmptyReasoningTail, string.Empty, StringComparison.Ordinal);
            if (pruned.Length != json.Length)
            {
                json = pruned;
                literallyChanged = true;
                hasReasoning = json.Contains(QuotedReasoningKey, StringComparison.Ordinal);
                if (!json.Contains(FinishReasonKey, StringComparison.Ordinal) && !hasToolCalls && !hasReasoning)
                    return json;
            }
        }

        // 商汤的每个增量块都带 "finish_reason":""（一轮上千块），逐块建 DOM 再序列化是流式里最大的一笔分配。
        // finish_reason 只有 null 或空串两种形态时按字面处理，其余（带空格、未知值、tool_calls）仍走 DOM
        if (!hasToolCalls && !hasReasoning)
        {
            int keys = CountOf(json, QuotedFinishReasonKey);
            int nulls = CountOf(json, NullFinishReason);
            int empties = CountOf(json, EmptyFinishReason);
            if (keys == nulls) return literallyChanged ? json : null;
            if (keys == nulls + empties) return json.Replace(EmptyFinishReason, NullFinishReason, StringComparison.Ordinal);
        }

        try
        {
            var node = JsonNode.Parse(json);
            if (!FixNode(node) && !literallyChanged) return null;
            return node!.ToJsonString();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static int CountOf(string text, string value)
    {
        int count = 0;
        for (int index = text.IndexOf(value, StringComparison.Ordinal);
             index >= 0;
             index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }

    /// <summary>
    /// 字面改名 <c>"reasoning":</c> → <c>"reasoning_content":</c>。只在三者齐备时动手：
    /// 同块没有 <c>reasoning_content</c>（否则改完出现重复键）、<c>"reasoning":</c> 只出现一次、
    /// 值紧随非空字符串（<c>:</c> 后是 <c>"</c> 且不是紧闭的 <c>""</c>）。
    /// 拿不准的（双键、非字符串值、键后带空格、空串）一律返回 false，走 DOM 逐个看。
    /// </summary>
    private static bool TryRenameReasoningKey(string json, out string renamed)
    {
        renamed = json;
        if (json.Contains(QuotedReasoningContentKey, StringComparison.Ordinal)) return false;
        int first = json.IndexOf(ReasoningKeyPrefix, StringComparison.Ordinal);
        if (first < 0) return false;
        if (json.IndexOf(ReasoningKeyPrefix, first + ReasoningKeyPrefix.Length, StringComparison.Ordinal) >= 0)
            return false;
        int valueAt = first + ReasoningKeyPrefix.Length;
        if (valueAt + 1 >= json.Length || json[valueAt] != '"' || json[valueAt + 1] == '"') return false;
        renamed = json.Replace(ReasoningKeyPrefix, ReasoningContentKeyPrefix, StringComparison.Ordinal);
        return true;
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
                    // type 是个到处都有的键名,只在 tool_calls 数组的元素上认它,不做全局匹配
                    else if (pair.Key == ToolCallsKey && pair.Value is JsonArray toolCalls)
                    {
                        changed |= FixToolCallKinds(toolCalls);
                    }
                    // 商汤自研模型的思考字段：SDK 只认 reasoning_content，reasoning 会被静默丢掉。
                    // 非空且对端没有 reasoning_content 时改名；空串或已有 reasoning_content 时直接删掉，
                    // 思考只留一个来源（下游空思考本来也会被 ChatContentNormalizer 丢掉，这里提前收敛）
                    else if (pair.Key == ReasoningKey && pair.Value is JsonValue reasoningValue &&
                             reasoningValue.TryGetValue<string>(out string? reasoningText))
                    {
                        obj.Remove(ReasoningKey);
                        if (!string.IsNullOrEmpty(reasoningText) && !obj.ContainsKey(ReasoningContentKey))
                            obj[ReasoningContentKey] = pair.Value;
                        changed = true;
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

    // 只改「存在但不是 function」的 type。缺 type 的增量 chunk 不补:
    // 流式里后续 chunk 本来就只带 index 与 arguments 增量,给它硬塞一个 type 是在改协议语义
    private static bool FixToolCallKinds(JsonArray toolCalls)
    {
        var changed = false;
        foreach (var call in toolCalls)
        {
            if (call is not JsonObject obj) continue;
            if (!obj.TryGetPropertyValue(TypeKey, out var typeNode) || typeNode == null) continue;
            if (typeNode is JsonValue value && value.TryGetValue<string>(out var text) &&
                string.Equals(text, FunctionToolCall, StringComparison.Ordinal))
            {
                continue;
            }

            obj[TypeKey] = JsonValue.Create(FunctionToolCall);
            changed = true;
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
