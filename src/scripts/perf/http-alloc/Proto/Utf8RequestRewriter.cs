using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Net;

namespace HttpAllocBench.Proto;

/// <summary>
/// P2 原型：与 OpenAICompatibleRequestRewriter 同语义的字节级改写，不建 DOM。
/// 根对象按「保留的属性原样拷贝 + 要设的键追加在末尾」重建；
/// messages 只在要修参数或回填思考时才逐条走进去，其余子树一律 Skip（只扫描）。
/// 落地时内层改动可改用 Core 里的 Utf8JsonSplice（它支持插入：起点等于终点）
/// </summary>
internal static class Utf8RequestRewriter
{
    // 与现状 CompactJson 同口径：宽松编码器，中文原样
    private static readonly JsonWriterOptions WriterOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [ThreadStatic] private static ArrayBufferWriter<byte>? _scratch; //一个线程复用一套缓冲写插入值
    [ThreadStatic] private static Utf8JsonWriter? _scratchWriter;

    private readonly record struct InnerEdit(int Start, int End, byte[] Insert);

    private readonly record struct RootProperty(int Start, int End);

    /// <summary>
    /// 按模型配置与请求上下文改写请求体
    /// </summary>
    /// <param name="json">SDK 序列化出的请求体</param>
    /// <param name="extraParams">模型的额外参数</param>
    /// <param name="forbidToolCalls">是否写 tool_choice: none</param>
    /// <param name="reasoningByCallId">按 tool_call id 回填的思考；不需要回填时为 null</param>
    /// <param name="omitSamplingParams">是否删采样参数</param>
    /// <param name="into">给了就写进它并返回空数组；不给就返回新数组</param>
    /// <returns>无需改写或不是 JSON 对象时为 null</returns>
    public static byte[]? Rewrite(ReadOnlySpan<byte> json, IReadOnlyList<KeyValuePair<string, JsonNode?>>? extraParams,
        bool forbidToolCalls, IReadOnlyDictionary<string, string>? reasoningByCallId, bool omitSamplingParams,
        IBufferWriter<byte>? into = null)
    {
        bool needsArgFix = json.IndexOf("\"arguments\":\"null\""u8) >= 0 || json.IndexOf("\"arguments\": \"null\""u8) >= 0;
        bool restore = reasoningByCallId is { Count: > 0 };
        if (extraParams is not { Count: > 0 } && !forbidToolCalls && !needsArgFix && !restore && !omitSamplingParams)
            return null;

        // 要追加的根键（同名后者覆盖前者，与 DOM 上连续赋值一致）
        List<(string Key, byte[] Value)> added = [];
        if (extraParams != null)
        {
            foreach (var pair in extraParams) Upsert(added, pair.Key, Serialize(pair.Value));
        }

        if (forbidToolCalls) Upsert(added, "tool_choice", "\"none\""u8.ToArray());

        var reader = new Utf8JsonReader(json);
        try
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return null;

            List<RootProperty> kept = [];
            List<InnerEdit> edits = [];
            while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
            {
                int start = (int)reader.TokenStartIndex;
                bool drop = IsAdded(ref reader, added) || (omitSamplingParams && IsSampling(ref reader));
                bool isMessages = reader.ValueTextEquals("messages"u8);
                reader.Read();
                if (isMessages && !drop && (needsArgFix || restore) && reader.TokenType == JsonTokenType.StartArray)
                    ProcessMessages(ref reader, needsArgFix, restore ? reasoningByCallId : null, edits);
                else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                    reader.Skip();

                if (!drop) kept.Add(new RootProperty(start, (int)reader.BytesConsumed));
            }

            return Build(json, kept, edits, added, into);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ProcessMessages(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoningByCallId, List<InnerEdit> edits)
    {
        int arrayDepth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == arrayDepth) return;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                if (reader.TokenType == JsonTokenType.StartArray) reader.Skip();
                continue;
            }

            ProcessMessage(ref reader, fixArguments, reasoningByCallId, edits);
        }
    }

    // 回填思考：消息里没有非空的 reasoning_content、且某个 tool_call 的 id 命中时，插在消息对象收尾处；
    // 已有 "reasoning_content":null 则只换值（与 DOM 版 messageObj["reasoning_content"] = text 一致）
    private static void ProcessMessage(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoningByCallId, List<InnerEdit> edits)
    {
        int depth = reader.CurrentDepth;
        bool reasoningPresent = false;
        int reasoningNullStart = -1;
        int reasoningNullEnd = -1;
        string? matched = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == depth)
            {
                if (reasoningByCallId != null && !reasoningPresent && matched != null)
                {
                    if (reasoningNullStart >= 0)
                    {
                        edits.Add(new InnerEdit(reasoningNullStart, reasoningNullEnd, SerializeString(matched, withKey: false)));
                    }
                    else
                    {
                        int at = (int)reader.TokenStartIndex;
                        edits.Add(new InnerEdit(at, at, SerializeString(matched, withKey: true)));
                    }
                }

                return;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                continue;
            }

            if (reader.ValueTextEquals("reasoning_content"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.Null)
                {
                    reasoningNullStart = (int)reader.TokenStartIndex;
                    reasoningNullEnd = (int)reader.BytesConsumed;
                }
                else
                {
                    reasoningPresent = true;
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                }
            }
            else if (reader.ValueTextEquals("tool_calls"u8))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                    ProcessToolCalls(ref reader, fixArguments, reasoningByCallId, ref matched, edits);
                else if (reader.TokenType == JsonTokenType.StartObject) reader.Skip();
            }
            else
            {
                reader.Read();
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
            }
        }
    }

    private static void ProcessToolCalls(ref Utf8JsonReader reader, bool fixArguments,
        IReadOnlyDictionary<string, string>? reasoningByCallId, ref string? matched, List<InnerEdit> edits)
    {
        int arrayDepth = reader.CurrentDepth;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray && reader.CurrentDepth == arrayDepth) return;
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                if (reader.TokenType == JsonTokenType.StartArray) reader.Skip();
                continue;
            }

            int callDepth = reader.CurrentDepth;
            while (reader.Read() && !(reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == callDepth))
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                if (reader.ValueTextEquals("id"u8))
                {
                    reader.Read();
                    if (matched == null && reasoningByCallId != null && reader.TokenType == JsonTokenType.String &&
                        reasoningByCallId.TryGetValue(reader.GetString()!, out string? text)) matched = text;
                    else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                }
                else if (fixArguments && reader.ValueTextEquals("function"u8))
                {
                    reader.Read();
                    if (reader.TokenType != JsonTokenType.StartObject)
                    {
                        if (reader.TokenType == JsonTokenType.StartArray) reader.Skip();
                        continue;
                    }

                    FixArguments(ref reader, edits);
                }
                else
                {
                    reader.Read();
                    if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
                }
            }
        }
    }

    // 模型偶发把无参调用的 arguments 写成字符串 "null"：换成 "{}"
    private static void FixArguments(ref Utf8JsonReader reader, List<InnerEdit> edits)
    {
        int functionDepth = reader.CurrentDepth;
        while (reader.Read() && !(reader.TokenType == JsonTokenType.EndObject && reader.CurrentDepth == functionDepth))
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            bool isArguments = reader.ValueTextEquals("arguments"u8);
            reader.Read();
            if (isArguments && reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("null"u8))
                edits.Add(new InnerEdit((int)reader.TokenStartIndex, (int)reader.BytesConsumed, "\"{}\""u8.ToArray()));
            else if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray) reader.Skip();
        }
    }

    private static byte[] Build(ReadOnlySpan<byte> json, List<RootProperty> kept, List<InnerEdit> edits,
        List<(string Key, byte[] Value)> added, IBufferWriter<byte>? into)
    {
        edits.Sort(static (a, b) => a.Start.CompareTo(b.Start));
        List<byte[]> addedBytes = new(added.Count);
        foreach (var (key, value) in added) addedBytes.Add([.. Serialize(JsonValue.Create(key)), (byte)':', .. value]);

        int size = 2 + Math.Max(0, kept.Count + addedBytes.Count - 1); //花括号 + 属性间逗号
        foreach (RootProperty property in kept) size += property.End - property.Start;
        foreach (InnerEdit edit in edits) size += edit.Insert.Length - (edit.End - edit.Start);
        foreach (byte[] property in addedBytes) size += property.Length;

        byte[]? array = into == null ? GC.AllocateUninitializedArray<byte>(size) : null;
        Span<byte> output = array ?? into!.GetSpan(size);
        int written = 0;
        int editIndex = 0;
        output[written++] = (byte)'{';
        bool first = true;
        foreach (RootProperty property in kept)
        {
            if (!first) output[written++] = (byte)',';
            first = false;
            int cursor = property.Start;
            while (editIndex < edits.Count && edits[editIndex].Start < property.End)
            {
                InnerEdit edit = edits[editIndex++];
                json[cursor..edit.Start].CopyTo(output[written..]);
                written += edit.Start - cursor;
                edit.Insert.CopyTo(output[written..]);
                written += edit.Insert.Length;
                cursor = edit.End;
            }

            json[cursor..property.End].CopyTo(output[written..]);
            written += property.End - cursor;
        }

        foreach (byte[] property in addedBytes)
        {
            if (!first) output[written++] = (byte)',';
            first = false;
            property.CopyTo(output[written..]);
            written += property.Length;
        }

        output[written++] = (byte)'}';
        if (into == null) return array!;
        into.Advance(written);
        return [];
    }

    private static bool IsAdded(ref Utf8JsonReader reader, List<(string Key, byte[] Value)> added)
    {
        foreach (var (key, _) in added)
        {
            if (reader.ValueTextEquals(key)) return true;
        }

        return false;
    }

    private static bool IsSampling(ref Utf8JsonReader reader)
    {
        foreach (string key in OpenAICompatibleRequestRewriter.SamplingParamKeys)
        {
            if (reader.ValueTextEquals(key)) return true;
        }

        return false;
    }

    private static void Upsert(List<(string Key, byte[] Value)> added, string key, byte[] value)
    {
        int index = added.FindIndex(x => x.Key == key);
        if (index >= 0) added[index] = (key, value);
        else added.Add((key, value));
    }

    private static byte[] SerializeString(string value, bool withKey)
    {
        var buffer = _scratch ??= new ArrayBufferWriter<byte>(1024);
        buffer.ResetWrittenCount();
        var writer = _scratchWriter ??= new Utf8JsonWriter(buffer, WriterOptions);
        writer.Reset(buffer);
        if (withKey) buffer.Write(",\"reasoning_content\":"u8);
        writer.WriteStringValue(value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    private static byte[] Serialize(JsonNode? node)
    {
        if (node == null) return "null"u8.ToArray();
        var buffer = new ArrayBufferWriter<byte>(64);
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions)) node.WriteTo(writer);
        return buffer.WrittenSpan.ToArray();
    }
}
