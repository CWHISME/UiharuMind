/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net.Json;

namespace UiharuMind.Core.AI.Net.Request;

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

    // 宽松编码器与 SDK 自己序列化请求体的口径一致：默认编码器会把中文全写成 \uXXXX，
    // 每次改写后发出去的请求体凭空大三成（中文为主的长会话实测 458KB → 595KB）
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const int MaxRetainedScratch = 64 * 1024; //超过就不留:几万字的思考会让这块缓冲常驻在线程池线程上

    [ThreadStatic] private static ArrayBufferWriter<byte>? _scratch; //序列化插入值用,每个线程一套
    [ThreadStatic] private static Utf8JsonWriter? _scratchWriter;

    private readonly record struct Region(int Start, int End);

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
        IReadOnlyList<string?>? reasoning = rewrite.ReasoningByAssistantMessage is { Count: > 0 } list ? list : null;
        IReadOnlyList<string?>? tickets = rewrite.ReasoningTickets is { Count: > 0 } ticketList ? ticketList : null;
        if (rewrite.ExtraParams is not { Count: > 0 } && !rewrite.ForbidToolCalls && !fixArguments && reasoning == null &&
            !rewrite.OmitSamplingParams && !rewrite.FillEmptyReasoning && rewrite.PromptCacheKey == null &&
            tickets == null)
        {
            return false;
        }

        // 要追加到根上的键,同名后者覆盖前者(与在 DOM 上连续赋值一致)
        List<(string Key, byte[] Property)> added = [];
        // 先写,让供应商额外参数能覆盖它(显式配置优先于这里推出的值)
        if (rewrite.PromptCacheKey != null) Upsert(added, "prompt_cache_key", rewrite.PromptCacheKey);
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
            List<Utf8JsonSplice.Edit> edits = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int start = (int)reader.TokenStartIndex;
                bool drop = IsAdded(ref reader, added) || rewrite.OmitSamplingParams && IsSampling(ref reader);
                bool isMessages = reader.ValueTextEquals("messages"u8);
                reader.Read();
                // 被额外参数覆盖掉的 messages 不能扫:记下的改动会落到别的属性上
                if (isMessages && !drop &&
                    (fixArguments || reasoning != null || rewrite.FillEmptyReasoning || tickets != null) &&
                    reader.TokenType == JsonTokenType.StartArray)
                    ScanMessages(ref reader, fixArguments, reasoning, rewrite.FillEmptyReasoning, rewrite.ReasoningField,
                        tickets, edits);
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
        IReadOnlyList<string?>? reasoning, bool fillEmptyReasoning, string reasoningField,
        IReadOnlyList<string?>? tickets, List<Utf8JsonSplice.Edit> edits)
    {
        int assistantIndex = 0; //第几条助手消息,与 reasoning 列表一一对应
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                ScanMessage(ref reader, fixArguments, reasoning, fillEmptyReasoning, reasoningField, tickets, edits,
                    ref assistantIndex);
            else SkipValue(ref reader);
        }
    }

    /// <summary>
    /// 助手消息的思考正文回填。标准 ChatMessage→wire 消息转换认不出 <c>TextReasoningContent</c>,
    /// 序列化时会把它悄悄丢掉,只能从清单
    /// <see cref="RequestRewrite.ReasoningByAssistantMessage"/>(源头是
    /// <see cref="LlmRequestContext.PendingReasoningSource"/>)按<b>助手消息出现顺序</b>找回来补上。已经带了(哪怕是空串)就不覆盖,只有缺省或 null 才补。
    ///
    /// <b>有正文就发,不挑消息</b>:服务端给过的思考原样带回去,工具调用链之外的回复也一样——
    /// 各家的校验口径不同（DeepSeek 只查带 tool_calls 的，网关连纯文本回复都查），
    /// 全都带回去就同时满足所有口径，不用去猜这一家要哪一种。
    ///
    /// 历史里压根没有正文时，<paramref name="fillEmptyReasoning"/> 决定要不要补空串
    /// （服务端要的是「字段必须在」，不是「字段必须有内容」）。
    /// </summary>
    private static void ScanMessage(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyList<string?>? reasoning, bool fillEmptyReasoning, string reasoningField,
        IReadOnlyList<string?>? tickets, List<Utf8JsonSplice.Edit> edits, ref int assistantIndex)
    {
        bool isAssistant = false;
        bool hasReasoning = false;
        Region nullReasoning = new(-1, -1); //思考键为 null 的值区间,回填时只换值

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("role"u8))
            {
                reader.Read();
                isAssistant = reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("assistant"u8);
                SkipValue(ref reader);
            }
            else if (reader.ValueTextEquals(reasoningField))
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
                if (reader.TokenType == JsonTokenType.StartArray) ScanToolCalls(ref reader, fixArguments, edits);
                else SkipValue(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        // 此时停在消息对象的 '}' 上。序号必须无条件递增(已带正文的消息同样占一个位置),
        // 否则后面每条都会错位取到上一条的思考
        int index = isAssistant ? assistantIndex++ : -1;

        // 加密票据按位置原样拼回去。与明文那条互不影响:同一条消息可能两个都有。
        // 能不能发（同模型门禁）在收集侧就判过了，这里只管字节
        if (index >= 0 && tickets != null && index < tickets.Count && tickets[index] is { } ticket)
        {
            int ticketAt = (int)reader.TokenStartIndex;
            edits.Add(new Utf8JsonSplice.Edit(ticketAt, ticketAt, Encoding.UTF8.GetBytes($",\"reasoning_details\":{ticket}")));
        }

        if (index < 0 || hasReasoning) return;

        string? text = reasoning != null && index < reasoning.Count ? reasoning[index] : null;
        if (text == null && !fillEmptyReasoning) return;

        string value = text ?? string.Empty;
        if (nullReasoning.Start >= 0)
        {
            edits.Add(new Utf8JsonSplice.Edit(nullReasoning.Start, nullReasoning.End, SerializeReasoning(value, withKey: false, reasoningField)));
        }
        else
        {
            int at = (int)reader.TokenStartIndex;
            edits.Add(new Utf8JsonSplice.Edit(at, at, SerializeReasoning(value, withKey: true, reasoningField)));
        }
    }

    private static void ScanToolCalls(ref Utf8JsonReader reader, bool fixArguments, List<Utf8JsonSplice.Edit> edits)
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
                bool isFunction = fixArguments && reader.ValueTextEquals("function"u8);
                reader.Read();
                if (isFunction && reader.TokenType == JsonTokenType.StartObject) FixArguments(ref reader, edits);
                else SkipValue(ref reader);
            }
        }
    }

    /// <summary>
    /// 模型偶发地把无参调用的 arguments 序列化成字面字符串 "null"(而非空对象 "{}")。
    /// 部分 OpenAI 兼容后端会对历史里的 arguments 做 json.loads 后 .items(),
    /// 解析出 None 就直接 400——'NoneType' object has no attribute 'items'。
    /// 修的是发出去的历史,不影响这次调用本身的执行结果。
    /// </summary>
    private static void FixArguments(ref Utf8JsonReader reader, List<Utf8JsonSplice.Edit> edits)
    {
        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            bool isArguments = reader.ValueTextEquals("arguments"u8);
            reader.Read();
            if (isArguments && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("null"u8))
                edits.Add(new Utf8JsonSplice.Edit((int)reader.TokenStartIndex, (int)reader.BytesConsumed, "\"{}\""u8.ToArray()));
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
    private static void Build(ReadOnlySpan<byte> json, List<Region> kept, List<Utf8JsonSplice.Edit> edits,
        List<(string Key, byte[] Property)> added, IBufferWriter<byte> output)
    {
        int size = 2 + Math.Max(0, kept.Count + added.Count - 1); //花括号 + 属性间的逗号
        foreach (Region property in kept) size += property.End - property.Start;
        foreach (Utf8JsonSplice.Edit edit in edits) size += edit.Replacement.Length - (edit.End - edit.Start);
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
                Utf8JsonSplice.Edit edit = edits[editIndex];
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
        foreach (string key in RequestRewrite.SamplingParamKeys)
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

    private static byte[] SerializeReasoning(string text, bool withKey, string reasoningField)
    {
        // 键名走 WritePropertyName 而不是拼进前缀字符串:字段名是运行时观测来的外部值，
        // 不该有拼出坏 JSON 的机会
        Utf8JsonWriter writer = BeginScratch(ReadOnlySpan<byte>.Empty);
        if (withKey)
        {
            writer.WriteStartObject();
            writer.WritePropertyName(reasoningField);
        }

        writer.WriteStringValue(text);
        if (withKey) writer.WriteEndObject();
        return EndScratch(writer, trimBraces: withKey, prefixComma: withKey);
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

    /// <param name="writer">刚写完内容的 scratch writer</param>
    /// <param name="trimBraces">去掉首尾的花括号（对象拆成属性）</param>
    /// <param name="prefixComma">结果前面补一个逗号（插到已有对象的末尾时用）</param>
    private static byte[] EndScratch(Utf8JsonWriter writer, bool trimBraces, bool prefixComma = false)
    {
        writer.Flush();
        ReadOnlySpan<byte> written = _scratch!.WrittenSpan;
        ReadOnlySpan<byte> body = trimBraces ? written[1..^1] : written;
        byte[] result = new byte[body.Length + (prefixComma ? 1 : 0)];
        if (prefixComma) result[0] = (byte)',';
        body.CopyTo(result.AsSpan(prefixComma ? 1 : 0));
        if (_scratch.Capacity > MaxRetainedScratch)
        {
            _scratch = null;
            _scratchWriter = null;
        }

        return result;
    }
}
