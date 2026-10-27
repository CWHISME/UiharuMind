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
/// <param name="ReasoningField">思考正文回填的 wire 键名（观测/预设/默认）</param>
/// <param name="FillEmptyReasoning">
/// 带 tool_calls 的助手消息即使没有思考正文，也要补一个空串字段（DeepSeek 类缺字段即 400）。
/// 仅在本次请求可能处于思考模式（<see cref="EThinkingMode.None"/> 之外）时为真。
/// </param>
internal readonly record struct RequestRewrite(
    IReadOnlyList<KeyValuePair<string, JsonNode?>>? ExtraParams,
    bool ForbidToolCalls,
    IReadOnlyDictionary<string, string>? ReasoningByCallId,
    bool OmitSamplingParams,
    bool FillEmptyReasoning = false,
    string ReasoningField = OpenAICompatibleRequestRewriter.DefaultReasoningField);

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

    /// <summary>模型没给回填键名时的默认 wire 键名</summary>
    internal const string DefaultReasoningField = "reasoning_content";

    // 宽松编码器与 SDK 自己序列化请求体的口径一致：默认编码器会把中文全写成 \uXXXX，
    // 每次改写后发出去的请求体凭空大三成（中文为主的长会话实测 458KB → 595KB）
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private const int MaxRetainedScratch = 64 * 1024; //超过就不留:几万字的思考会让这块缓冲常驻在线程池线程上

    [ThreadStatic] private static ArrayBufferWriter<byte>? _scratch; //序列化插入值用,每个线程一套
    [ThreadStatic] private static Utf8JsonWriter? _scratchWriter;

    private readonly record struct Region(int Start, int End);

    /// <summary>
    /// 按模型配置与本次请求上下文（<see cref="LlmRequestContext"/>）收集要做的改写
    /// </summary>
    /// <param name="model">目标模型；为 null 时只做与模型无关的改写</param>
    /// <returns>要做的改写</returns>
    public static RequestRewrite For(ILlmModel? model) => new(
        model?.GetExtraParams(),
        LlmRequestContext.ForbidToolCalls,
        // null/true 都收集思考:null=自动(历史有思考就回传),true=模型强制要求;
        // 收集器扫不到思考时返回空,自然不带字段——只有显式 false(确认不要求回传)或无模型才跳过。
        // 注意不能用 `model?.X != false`:nullable 的 lifted 比较在 null 时得到 null(判不出),
        // 这里用 `is not false`(null 也算)并先把 model 判空
        model is not null && model.ReplayExistingReasoning is not false
            ? LlmRequestContext.PendingReasoningSource?.Invoke()
            : null,
        // 采样参数固定的模型(如 Kimi)会拒绝显式传值的请求,开启后删掉这四个字段,不碰其它参数
        model?.OmitSamplingParams == true,
        // 没有思考正文也要带字段(空串):DeepSeek 类把「思考模式下带 tool_calls 的助手消息必须含思考字段」
        // 当硬校验,历史里那些关思考时跑出来的旧工具调用没有正文可回传,缺字段就是 400。
        // 但这条校验只在本次请求处于思考模式时成立——None 档显式发了 thinking:disabled,服务端按正文
        // 调用处理,不要求字段;此时补空串是多写字段、白改请求体。Default 不干预、由模型自己决定是否
        // 思考,仍按「可能思考」保守补上。
        model is not null && model.RequireReasoningFieldWhenEmpty &&
        model.ThinkingMode != EThinkingMode.None,
        // 思考回填键名不写死:按模型实际字段(运行时观测/预设/默认)回传
        ResolveReasoningField(model));

    /// <summary>
    /// 回填键名取模型给的生效值；模型为 null 或给了空串时退回
    /// <see cref="DefaultReasoningField"/>——空串拼进请求体就是 `"": "..."`，服务端认不出
    /// </summary>
    private static string ResolveReasoningField(ILlmModel? model) =>
        string.IsNullOrWhiteSpace(model?.ThinkingField) ? DefaultReasoningField : model.ThinkingField;

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
            !rewrite.OmitSamplingParams && !rewrite.FillEmptyReasoning)
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
            List<Utf8JsonSplice.Edit> edits = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int start = (int)reader.TokenStartIndex;
                bool drop = IsAdded(ref reader, added) || rewrite.OmitSamplingParams && IsSampling(ref reader);
                bool isMessages = reader.ValueTextEquals("messages"u8);
                reader.Read();
                // 被额外参数覆盖掉的 messages 不能扫:记下的改动会落到别的属性上
                if (isMessages && !drop && (fixArguments || reasoning != null || rewrite.FillEmptyReasoning) &&
                    reader.TokenType == JsonTokenType.StartArray)
                    ScanMessages(ref reader, fixArguments, reasoning, rewrite.FillEmptyReasoning, rewrite.ReasoningField, edits);
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
        IReadOnlyDictionary<string, string>? reasoning, bool fillEmptyReasoning, string reasoningField,
        List<Utf8JsonSplice.Edit> edits)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType == JsonTokenType.StartObject)
                ScanMessage(ref reader, fixArguments, reasoning, fillEmptyReasoning, reasoningField, edits);
            else SkipValue(ref reader);
        }
    }

    /// <summary>
    /// 思考模式下,助手消息带 tool_calls 时接口要求原样带回当时的思考正文,
    /// 否则报 "If thinking mode and tool_calls, reasoning_content must be passed back to the API"。
    /// 键名按 <paramref name="reasoningField"/>（模型实际字段，默认 reasoning_content）。
    /// 标准 ChatMessage→wire 消息转换认不出 <c>TextReasoningContent</c>,序列化时会把它悄悄丢掉,
    /// 只能按 tool_call id 从 <see cref="LlmRequestContext.PendingReasoningSource"/> 找回来补上。
    /// 一条消息可带多个 tool_calls,任一 id 命中即恢复整条消息的思考正文;已经带了(哪怕是空串)就不覆盖,只有缺省或 null 才补。
    ///
    /// 找不回正文时(<paramref name="fillEmptyReasoning"/> 为真)补一个空串:服务端要的是"字段必须在",
    /// 不是"字段必须有内容"。历史里那些关思考档位跑出来的工具调用没有正文可回传,缺字段就是 400。
    /// <paramref name="fillEmptyReasoning"/> 由调用方按「本次请求是否可能处于思考模式」把关:
    /// None 档已显式关思考,服务端不要求该字段,不必补。
    /// 只对带 tool_calls 的助手消息补——给普通消息塞空字段是白改请求体,还会打乱前缀缓存。
    /// </summary>
    private static void ScanMessage(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoning, bool fillEmptyReasoning, string reasoningField,
        List<Utf8JsonSplice.Edit> edits)
    {
        bool hasReasoning = false;
        bool hasToolCalls = false;
        Region nullReasoning = new(-1, -1); //思考键为 null 的值区间,回填时只换值
        string? matched = null;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals(reasoningField))
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
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    hasToolCalls = true;
                    ScanToolCalls(ref reader, fixArguments, reasoning, ref matched, edits);
                }
                else SkipValue(ref reader);
            }
            else
            {
                reader.Read();
                SkipValue(ref reader);
            }
        }

        // 此时停在消息对象的 '}' 上
        if (hasReasoning) return;
        if (matched == null && !(fillEmptyReasoning && hasToolCalls)) return;

        string text = matched ?? string.Empty;
        if (nullReasoning.Start >= 0)
        {
            edits.Add(new Utf8JsonSplice.Edit(nullReasoning.Start, nullReasoning.End, SerializeReasoning(text, withKey: false, reasoningField)));
        }
        else
        {
            int at = (int)reader.TokenStartIndex;
            edits.Add(new Utf8JsonSplice.Edit(at, at, SerializeReasoning(text, withKey: true, reasoningField)));
        }
    }

    private static void ScanToolCalls(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoning, ref string? matched, List<Utf8JsonSplice.Edit> edits)
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
