/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 一次请求体改写要做的事
/// </summary>
/// <param name="ExtraParams">模型的额外参数，写到根上（同名覆盖）</param>
/// <param name="ForbidToolCalls">写 <c>tool_choice: "none"</c></param>
/// <param name="ReasoningByCallId">按 tool_call id 回填的思考正文；不需要回填时为 null</param>
/// <param name="OmitSamplingParams">删掉 <see cref="OpenAICompatibleRequestRewriter.SamplingParamKeys"/></param>
internal readonly record struct RequestRewrite(
    IReadOnlyList<KeyValuePair<string, JsonNode?>>? ExtraParams,
    bool ForbidToolCalls,
    IReadOnlyDictionary<string, string>? ReasoningByCallId,
    bool OmitSamplingParams);

/// <summary>
/// 发往 OpenAI 兼容端点之前对请求体的几道改写：注入模型的额外参数、禁止工具调用、
/// 修畸形的工具参数、回填思考正文、删采样参数。
///
/// 直接在 UTF-8 字节上做，不建 DOM：根对象按「保留的属性原样拷贝 + 要设的键追加在末尾」重拼，
/// <c>messages</c> 只在要修参数或回填思考时逐条走进去，其余子树只扫不拷。原文的转义与空白一字不动。
/// 根上被覆盖的键会挪到末尾；JSON 语义不变，前缀缓存按渲染后的 token 算，不受影响。
/// </summary>
internal static class OpenAICompatibleRequestRewriter
{
    /// <summary>
    /// 开启 <c>OmitSamplingParams</c> 的模型要删掉的采样参数。只删这四个,
    /// <c>max_tokens/thinking/tool_choice</c> 等不受影响。
    /// </summary>
    internal static readonly string[] SamplingParamKeys =
        ["temperature", "top_p", "presence_penalty", "frequency_penalty"];

    // 宽松编码器与 SDK 自己序列化请求体的口径一致：默认编码器会把中文全写成 \uXXXX，
    // 每次改写后发出去的请求体凭空大三成（中文为主的长会话实测 458KB → 595KB）
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [ThreadStatic] private static ArrayBufferWriter<byte>? _scratch; //序列化插入值用,每个线程一套
    [ThreadStatic] private static Utf8JsonWriter? _scratchWriter;

    private readonly record struct Edit(int Start, int End, byte[] Replacement);

    private readonly record struct Region(int Start, int End);

    /// <summary>
    /// 按模型配置与本次请求上下文（<see cref="LlmRequestContext"/>）收集要做的改写
    /// </summary>
    /// <param name="model">目标模型；为 null 时只做与模型无关的改写</param>
    /// <returns>要做的改写</returns>
    public static RequestRewrite For(ILlmModel? model) => new(
        model?.GetExtraParams(),
        LlmRequestContext.ForbidToolCalls,
        // 只对已确认要求 reasoning_content 回填的模型(目前只有 DeepSeek)才去收集,
        // 其余共用 thinking/reasoning_effort 参数的兼容服务不无谓塞多余字段
        model?.RequiresReasoningContentRoundtrip == true ? LlmRequestContext.PendingReasoningSource?.Invoke() : null,
        // 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,开启后删掉这四个字段,不碰其它参数
        model?.OmitSamplingParams == true);

    /// <summary>
    /// 改写请求体
    /// </summary>
    /// <param name="json">SDK 序列化出的请求体</param>
    /// <param name="rewrite">要做的改写</param>
    /// <param name="output">改写后的请求体写到这里</param>
    /// <returns>是否写了改写结果；无需改写、不是 JSON 对象时为 false，output 不动，照原样发</returns>
    public static bool Rewrite(ReadOnlySpan<byte> json, in RequestRewrite rewrite, IBufferWriter<byte> output)
    {
        // 大多数请求不含畸形 tool_calls 参数,先做一次廉价的字节扫描,避免每次都逐条走 messages
        bool fixArguments = json.IndexOf("\"arguments\":\"null\""u8) >= 0 || json.IndexOf("\"arguments\": \"null\""u8) >= 0;
        IReadOnlyDictionary<string, string>? reasoning = rewrite.ReasoningByCallId is { Count: > 0 } map ? map : null;
        if (rewrite.ExtraParams is not { Count: > 0 } && !rewrite.ForbidToolCalls && !fixArguments && reasoning == null &&
            !rewrite.OmitSamplingParams)
        {
            return false;
        }

        // 要追加到根上的键,同名后者覆盖前者(与在 DOM 上连续赋值一致)
        List<(string Key, byte[] Property)> added = [];
        if (rewrite.ExtraParams != null)
        {
            foreach (var pair in rewrite.ExtraParams) Upsert(added, pair.Key, pair.Value);
        }

        // 带着工具定义(前缀缓存要对齐)但不许调用。MEAI 的 ChatToolMode 没有 None,
        // 只能在这一层直接写进请求体
        if (rewrite.ForbidToolCalls) Upsert(added, "tool_choice", "none");

        try
        {
            var reader = new Utf8JsonReader(json);
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;

            List<Region> kept = [];
            List<Edit> edits = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int start = (int)reader.TokenStartIndex;
                bool drop = IsAdded(ref reader, added) || rewrite.OmitSamplingParams && IsSampling(ref reader);
                bool isMessages = reader.ValueTextEquals("messages"u8);
                reader.Read();
                if (isMessages && (fixArguments || reasoning != null) && reader.TokenType == JsonTokenType.StartArray)
                    ScanMessages(ref reader, fixArguments, reasoning, edits);
                else
                    SkipValue(ref reader);

                if (!drop) kept.Add(new Region(start, (int)reader.BytesConsumed));
            }

            Build(json, kept, edits, added, output);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void ScanMessages(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoning, List<Edit> edits)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject) ScanMessage(ref reader, fixArguments, reasoning, edits);
            else SkipValue(ref reader);
        }
    }

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时接口要求原样带回当时的 reasoning_content,
    /// 否则报 "If thinking mode and tool_calls, reasoning_content must be passed back to the API"。
    /// 标准 ChatMessage→wire 消息转换认不出 <c>TextReasoningContent</c>,序列化时会把它悄悄丢掉,
    /// 只能按 tool_call id 从 <see cref="LlmRequestContext.PendingReasoningSource"/> 找回来补上。
    /// 一条消息可带多个 tool_calls,任一 id 命中即恢复整条消息的思考正文;已经带了非空的就不覆盖。
    /// </summary>
    private static void ScanMessage(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoning, List<Edit> edits)
    {
        bool hasReasoning = false;
        Region nullReasoning = new(-1, -1); //"reasoning_content":null 的值区间,回填时只换值
        string? matched = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("reasoning_content"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.Null)
                    nullReasoning = new Region((int)reader.TokenStartIndex, (int)reader.BytesConsumed);
                else
                    hasReasoning = true;
                SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals("tool_calls"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray) ScanToolCalls(ref reader, fixArguments, reasoning, ref matched, edits);
                else SkipValue(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        // 此时停在消息对象的 '}' 上
        if (matched == null || hasReasoning) return;
        if (nullReasoning.Start >= 0)
        {
            edits.Add(new Edit(nullReasoning.Start, nullReasoning.End, SerializeReasoning(matched, withKey: false)));
        }
        else
        {
            int at = (int)reader.TokenStartIndex;
            edits.Add(new Edit(at, at, SerializeReasoning(matched, withKey: true)));
        }
    }

    private static void ScanToolCalls(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoning, ref string? matched, List<Edit> edits)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                SkipValue(ref reader);
                continue;
            }

            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (matched == null && reasoning != null && reader.TokenType == JsonTokenType.String &&
                        reasoning.TryGetValue(reader.GetString()!, out string? text))
                        matched = text;
                    else
                        SkipValue(ref reader);
                }
                else if (fixArguments && reader.ValueTextEquals("function"u8))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject) FixArguments(ref reader, edits);
                    else SkipValue(ref reader);
                }
                else
                {
                    reader.Read();
                    SkipValue(ref reader);
                }
            }
        }
    }

    /// <summary>
    /// 模型偶发地把无参调用的 arguments 序列化成字面字符串 "null"(而非空对象 "{}")。
    /// 部分 OpenAI 兼容后端会对历史里的 arguments 做 json.loads 后 .items(),
    /// 解析出 None 就直接 400——'NoneType' object has no attribute 'items'。
    /// 修的是发出去的历史,不影响这次调用本身的执行结果。
    /// </summary>
    private static void FixArguments(ref Utf8JsonReader reader, List<Edit> edits)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isArguments = reader.ValueTextEquals("arguments"u8);
            reader.Read();
            if (isArguments && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("null"u8))
                edits.Add(new Edit((int)reader.TokenStartIndex, (int)reader.BytesConsumed, "\"{}\""u8.ToArray()));
            else
                SkipValue(ref reader);
        }
    }

    // 停在值上时调用:容器整个跳过,标量什么都不用做
    private static void SkipValue(ref Utf8JsonReader reader)
    {
        if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
    }

    // '{' + 保留的属性(内层改动就地落下) + 追加的属性 + '}'
    private static void Build(ReadOnlySpan<byte> json, List<Region> kept, List<Edit> edits,
        List<(string Key, byte[] Property)> added, IBufferWriter<byte> output)
    {
        int size = 2 + Math.Max(0, kept.Count + added.Count - 1); //花括号 + 属性间的逗号
        foreach (Region property in kept) size += property.End - property.Start;
        foreach (Edit edit in edits) size += edit.Replacement.Length - (edit.End - edit.Start);
        foreach (var (_, property) in added) size += property.Length;

        // 回填思考在消息收尾时才记下,可能排在同一条消息里修参数那处之前
        edits.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        Span<byte> destination = output.GetSpan(size);
        int written = 0;
        int editIndex = 0;
        destination[written++] = (byte)'{';
        foreach (Region property in kept)
        {
            if (written > 1) destination[written++] = (byte)',';
            int cursor = property.Start;
            for (; editIndex < edits.Count && edits[editIndex].Start < property.End; editIndex++)
            {
                Edit edit = edits[editIndex];
                written += Copy(json[cursor..edit.Start], destination[written..]);
                written += Copy(edit.Replacement, destination[written..]);
                cursor = edit.End;
            }

            written += Copy(json[cursor..property.End], destination[written..]);
        }

        foreach (var (_, property) in added)
        {
            if (written > 1) destination[written++] = (byte)',';
            written += Copy(property, destination[written..]);
        }

        destination[written++] = (byte)'}';
        output.Advance(written);
    }

    private static int Copy(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        source.CopyTo(destination);
        return source.Length;
    }

    private static bool IsAdded(ref Utf8JsonReader reader, List<(string Key, byte[] Property)> added)
    {
        foreach (var (key, _) in added)
        {
            if (reader.ValueTextEquals(key)) return true;
        }

        return false;
    }

    private static bool IsSampling(ref Utf8JsonReader reader)
    {
        foreach (string key in SamplingParamKeys)
        {
            if (reader.ValueTextEquals(key)) return true;
        }

        return false;
    }

    private static void Upsert(List<(string Key, byte[] Property)> added, string key, JsonNode? value)
    {
        Utf8JsonWriter writer = BeginScratch(ReadOnlySpan<byte>.Empty);
        writer.WriteStartObject();
        writer.WritePropertyName(key);
        if (value == null) writer.WriteNullValue();
        else value.WriteTo(writer);
        writer.WriteEndObject();
        byte[] property = EndScratch(writer, trimBraces: true);

        int index = added.FindIndex(x => x.Key == key);
        if (index >= 0) added[index] = (key, property);
        else added.Add((key, property));
    }

    private static byte[] SerializeReasoning(string text, bool withKey)
    {
        Utf8JsonWriter writer = BeginScratch(withKey ? ",\"reasoning_content\":"u8 : ReadOnlySpan<byte>.Empty);
        writer.WriteStringValue(text);
        return EndScratch(writer, trimBraces: false);
    }

    private static Utf8JsonWriter BeginScratch(ReadOnlySpan<byte> prefix)
    {
        ArrayBufferWriter<byte> buffer = _scratch ??= new ArrayBufferWriter<byte>(1024);
        buffer.ResetWrittenCount();
        buffer.Write(prefix);
        Utf8JsonWriter writer = _scratchWriter ??= new Utf8JsonWriter(buffer, WriterOptions);
        writer.Reset(buffer);
        return writer;
    }

    private static byte[] EndScratch(Utf8JsonWriter writer, bool trimBraces)
    {
        writer.Flush();
        ReadOnlySpan<byte> written = _scratch!.WrittenSpan;
        return (trimBraces ? written[1..^1] : written).ToArray();
    }
}
