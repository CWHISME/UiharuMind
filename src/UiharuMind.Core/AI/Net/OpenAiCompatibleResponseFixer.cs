using System.Buffers;
using System.Text;
using System.Text.Json;

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
///
/// 流式响应每块都要过这里（网关每块都带 <c>"finish_reason":""</c>，一次调用上千块），
/// 所以只扫一遍 <see cref="Utf8JsonReader"/>、记下要改的字节区间再拼接（<see cref="Utf8JsonSplice"/>）：
/// 不建 DOM、不经字符串，没改到的部分一字不动。
/// </summary>
internal static class OpenAiCompatibleResponseFixer
{
    private const int MaxDepth = 64; //与 Utf8JsonReader 默认的嵌套上限一致

    private static readonly byte[] NullValue = "null"u8.ToArray();
    private static readonly byte[] StopValue = "\"stop\""u8.ToArray();
    private static readonly byte[] FunctionValue = "\"function\""u8.ToArray(); //OpenAI 规范里 tool_calls 的 type 只有这一个合法值
    private static readonly byte[] ReasoningContentKey = "\"reasoning_content\""u8.ToArray();

    private static readonly HashSet<string> ValidFinishReasons = new(StringComparer.Ordinal)
    {
        "stop", "length", "content_filter", "tool_calls", "function_call"
    };

    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ValidFinishReasonLookup =
        ValidFinishReasons.GetAlternateLookup<ReadOnlySpan<char>>();

    private enum Key : byte
    {
        Other,
        FinishReason,
        ToolCalls,
        Reasoning,
        ToolCallType,
    }

    // 一层容器的扫描状态。思考键要等对象收尾才能定夺：同对象的 reasoning_content 可能排在它后面
    private struct Frame
    {
        public bool IsToolCallsArray;
        public bool IsToolCall; //直接位于 tool_calls 数组里的元素
        public bool HasReasoningContent;
        public bool ReasoningNonEmpty;
        public int ReasoningKeyStart; //-1 表示本层没有字符串值的 reasoning 键
        public int ReasoningKeyEnd;
        public int ReasoningValueEnd;
    }

    /// <summary>
    /// 修正一行 SSE：只认 <c>data:</c> 行，<c>[DONE]</c> 与空载荷不动
    /// </summary>
    /// <param name="line">原始行（不含换行）</param>
    /// <param name="output">需要修正时写入 <c>data: </c> 与修正后的 JSON</param>
    /// <returns>写了返回 true；无需修正返回 false，output 不动，调用方原样转发</returns>
    public static bool TryFixEventStreamLine(ReadOnlySpan<byte> line, IBufferWriter<byte> output)
    {
        if (!line.StartsWith("data:"u8)) return false;
        ReadOnlySpan<byte> payload = line["data:"u8.Length..].Trim(" \t"u8);
        if (payload.IsEmpty || payload.SequenceEqual("[DONE]"u8)) return false;
        return TryFix(payload, output, "data: "u8);
    }

    /// <summary>
    /// 修正一段 chat completion 响应 JSON
    /// </summary>
    /// <param name="json">原始 JSON</param>
    /// <param name="output">需要修正时写入修正后的 JSON</param>
    /// <returns>写了返回 true；无需修正或解析失败返回 false，output 不动</returns>
    public static bool TryFix(ReadOnlySpan<byte> json, IBufferWriter<byte> output) => TryFix(json, output, default);

    /// <summary>
    /// 修正一行 SSE 文本（字符串入口，测试用）
    /// </summary>
    /// <param name="line">原始行，含 data: 前缀</param>
    /// <returns>需要修正时返回新行，否则原样返回</returns>
    public static string FixEventStreamLine(string line)
    {
        var output = new ArrayBufferWriter<byte>();
        return TryFixEventStreamLine(Encoding.UTF8.GetBytes(line), output)
            ? Encoding.UTF8.GetString(output.WrittenSpan)
            : line;
    }

    /// <summary>
    /// 修正一段 chat completion 响应 JSON（字符串入口，测试用）
    /// </summary>
    /// <param name="json">原始 JSON</param>
    /// <returns>需要修正时返回新 JSON，无需修正或解析失败返回 null</returns>
    public static string? FixJson(string json)
    {
        var output = new ArrayBufferWriter<byte>();
        return TryFix(Encoding.UTF8.GetBytes(json), output) ? Encoding.UTF8.GetString(output.WrittenSpan) : null;
    }

    private static bool TryFix(ReadOnlySpan<byte> json, IBufferWriter<byte> output, ReadOnlySpan<byte> prefix)
    {
        // 三个键名一个都不含就不必解析
        if (json.IndexOf("finish_reason"u8) < 0 && json.IndexOf("tool_calls"u8) < 0 &&
            json.IndexOf("\"reasoning\""u8) < 0) return false;

        var splice = new Utf8JsonSplice();
        try
        {
            if (!Collect(json, ref splice) || splice.Count == 0) return false;
        }
        catch (JsonException)
        {
            return false; //不是合法 JSON：原样交给 SDK
        }

        output.Write(prefix);
        splice.WriteTo(json, output);
        return true;
    }

    // 一遍扫描记下全部改动；同一对象里出现两个 reasoning 键这种拿不准的形态，整块不动
    private static bool Collect(ReadOnlySpan<byte> json, ref Utf8JsonSplice splice)
    {
        Span<Frame> frames = stackalloc Frame[MaxDepth + 1];
        var reader = new Utf8JsonReader(json);
        Key key = Key.Other;
        int keyStart = 0;
        int keyEnd = 0;
        int toolCallsDepth = 0;

        while (reader.Read())
        {
            int depth = reader.CurrentDepth;
            switch (reader.TokenType)
            {
                case JsonTokenType.PropertyName:
                {
                    ref Frame owner = ref frames[depth - 1];
                    keyStart = (int)reader.TokenStartIndex;
                    keyEnd = keyStart + reader.ValueSpan.Length + 2;
                    key = Classify(ref reader, ref owner, toolCallsDepth > 0);
                    if (key == Key.Reasoning && owner.ReasoningKeyStart >= 0) return false;
                    continue;
                }
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                {
                    if (key == Key.ToolCallType)
                    {
                        // type 是对象或数组：整个值换成 "function"
                        int start = (int)reader.TokenStartIndex;
                        reader.Skip();
                        splice.Replace(start, (int)reader.BytesConsumed, FunctionValue);
                        break;
                    }

                    bool isArray = reader.TokenType == JsonTokenType.StartArray;
                    frames[depth] = new Frame
                    {
                        ReasoningKeyStart = -1,
                        IsToolCallsArray = isArray && key == Key.ToolCalls,
                        IsToolCall = !isArray && depth > 0 && frames[depth - 1].IsToolCallsArray,
                    };
                    if (frames[depth].IsToolCallsArray) toolCallsDepth++;
                    break;
                }
                case JsonTokenType.EndObject:
                    ResolveReasoning(json, ref frames[depth], ref splice);
                    break;
                case JsonTokenType.EndArray:
                    if (frames[depth].IsToolCallsArray) toolCallsDepth--;
                    break;
                case JsonTokenType.String:
                    CollectString(ref reader, key, ref splice, frames, keyStart, keyEnd);
                    break;
                case JsonTokenType.Number:
                case JsonTokenType.True:
                case JsonTokenType.False:
                    if (key == Key.ToolCallType)
                        splice.Replace((int)reader.TokenStartIndex, (int)reader.BytesConsumed, FunctionValue);
                    break;
            }

            key = Key.Other;
        }

        return true;
    }

    // tool_calls 子树里只认元素自身的 type；finish_reason、reasoning 只在子树外修
    private static Key Classify(ref Utf8JsonReader reader, ref Frame owner, bool insideToolCalls)
    {
        if (insideToolCalls)
            return owner.IsToolCall && reader.ValueTextEquals("type"u8) ? Key.ToolCallType : Key.Other;
        if (reader.ValueTextEquals("finish_reason"u8)) return Key.FinishReason;
        if (reader.ValueTextEquals("tool_calls"u8)) return Key.ToolCalls;
        if (reader.ValueTextEquals("reasoning"u8)) return Key.Reasoning;
        if (reader.ValueTextEquals("reasoning_content"u8)) owner.HasReasoningContent = true;
        return Key.Other;
    }

    private static void CollectString(ref Utf8JsonReader reader, Key key, ref Utf8JsonSplice splice,
        scoped Span<Frame> frames, int keyStart, int keyEnd)
    {
        int start = (int)reader.TokenStartIndex;
        int end = (int)reader.BytesConsumed;
        switch (key)
        {
            case Key.FinishReason:
                if (FinishReasonFix(reader.ValueSpan, reader.ValueIsEscaped ? reader.GetString() : null) is { } fix)
                    splice.Replace(start, end, fix);
                break;
            case Key.ToolCallType:
                if (!reader.ValueTextEquals("function"u8)) splice.Replace(start, end, FunctionValue);
                break;
            case Key.Reasoning:
            {
                ref Frame owner = ref frames[reader.CurrentDepth - 1];
                owner.ReasoningKeyStart = keyStart;
                owner.ReasoningKeyEnd = keyEnd;
                owner.ReasoningValueEnd = end;
                owner.ReasoningNonEmpty = reader.ValueSpan.Length > 0;
                break;
            }
        }
    }

    // 空值/空白写回 null（本块还没结束），非空的未知值统一当作正常结束；合法值返回 null 表示不改
    private static byte[]? FinishReasonFix(ReadOnlySpan<byte> raw, string? unescaped)
    {
        if (raw.IsEmpty) return NullValue;
        Span<char> buffer = stackalloc char[64];
        scoped ReadOnlySpan<char> text;
        if (unescaped != null) text = unescaped;
        else if (raw.Length <= buffer.Length) text = buffer[..Encoding.UTF8.GetChars(raw, buffer)];
        else text = Encoding.UTF8.GetString(raw);
        if (text.IsWhiteSpace()) return NullValue;
        return ValidFinishReasonLookup.Contains(text) ? null : StopValue;
    }

    // 对象收尾时定夺思考键：非空且同对象没有 reasoning_content 时改名；空串或已有 reasoning_content 时删掉，
    // 思考只留一个来源（下游空思考本来也会被 ChatContentNormalizer 丢掉，这里提前收敛）
    private static void ResolveReasoning(ReadOnlySpan<byte> json, ref Frame frame, ref Utf8JsonSplice splice)
    {
        if (frame.ReasoningKeyStart < 0) return;
        if (frame.ReasoningNonEmpty && !frame.HasReasoningContent)
            splice.Replace(frame.ReasoningKeyStart, frame.ReasoningKeyEnd, ReasoningContentKey);
        else
            splice.RemoveProperty(json, frame.ReasoningKeyStart, frame.ReasoningValueEnd);
    }
}
