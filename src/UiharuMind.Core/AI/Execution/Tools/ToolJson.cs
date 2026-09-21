/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 数组参数的宽容读法：schema 说要数组，模型给了一个标量字符串时也照收。
///
/// 实机踩到的就是这个：<c>Grep(fileGlobs: "*.cpp")</c> 直接死在反序列化上，
/// 模型拿回的是 <c>The JSON value could not be converted to System.String[]</c>——
/// 一句它既看不懂也无从改正的框架异常（连哪个参数错了都没说）。
/// 「该给数组却给了标量」是模型的通病，不是这一个参数的问题，所以治在序列化层。
/// </summary>
public sealed class LenientStringArrayConverter : JsonConverter<string[]>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            string value = reader.GetString() ?? string.Empty;
            return value.Length == 0 ? [] : SplitLoosely(value);
        }

        if (reader.TokenType != JsonTokenType.StartArray)
        {
            throw new JsonException($"Expected a string or an array of strings, got {reader.TokenType}.");
        }

        List<string> items = [];
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            // 数组里混进非字符串(数字、布尔)时按其字面量收下,不为此整调用失败:
            // 一条不合规的 glob 顶多匹配不到,而整调用失败要多烧一轮
            items.Add(reader.TokenType == JsonTokenType.String
                ? reader.GetString() ?? string.Empty
                : JsonDocument.ParseValue(ref reader).RootElement.ToString());
        }

        return items.ToArray();
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (string item in value) writer.WriteStringValue(item);
        writer.WriteEndArray();
    }

    /// <summary>
    /// 一个标量字符串里塞了多项时按逗号拆开（<c>"*.cpp,*.h"</c>）。
    ///
    /// <b>带花括号的一律不拆</b>：glob 的分组语法本身就用逗号（<c>*.{cpp,h}</c>），
    /// 拆了会把一个写对了的表达式毁掉。
    /// </summary>
    private static string[] SplitLoosely(string value)
    {
        if (value.Contains('{')) return [value];

        string[] parts = value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? [value] : parts;
    }
}

/// <summary>
/// <c>List&lt;FileEdit&gt;</c> 的宽容读法：schema 说要数组，模型却给了单对象或一整段 JSON 字符串时也照收。
///
/// 实机踩到的三种形态（与 pi 的 <c>prepareArguments</c> 兼容层处理的是同一类模型怪癖）：
/// ✓ <c>edits</c> 整个传成一段 JSON 字符串（里面可能又套数组或单对象）；
/// ✓ <c>edits</c> 漏掉外层数组，直接传一个编辑对象；
/// ✓ 元素属性名写成 <c>old_string</c>/<c>new_string</c>（underscore 旧习惯）。
/// 属性名读取时手工兼容两种拼写，不依赖序列化选项。
/// </summary>
public sealed class LenientFileEditListConverter : JsonConverter<List<FileEdit>>
{
    public override List<FileEdit>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null) return null;

        if (reader.TokenType == JsonTokenType.String)
        {
            // 形态一：整个 edits 是一段 JSON 字符串——先按 JSON 解析一次再看是数组还是对象。
            // 模型把 edits 发成一段根本不像 JSON 的普通文本（"replace foo with bar"）时,
            // Parse 会抛底层 JsonException——那正是我们本批要消灭的"模型看不懂也无从改正"的失败。
            // 只在这里把"JSON 解析失败"转成话术;ReadElements 自己抛的语义异常(标量根/非对象元素)
            // 必须原样穿透,不能被这个 catch 吞掉改写成"non-JSON string"——那是另一个错误。
            string raw = reader.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(raw)) return [];

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(raw);
            }
            catch (JsonException ex)
            {
                throw new JsonException(
                    $"edits must be a JSON array of {{oldString,newString}} objects, a single such object, "
                    + $"or a JSON string containing one of those. Got a non-JSON string: '{Preview(raw)}'.", ex);
            }

            using (doc)
            {
                return ReadElements(doc.RootElement);
            }
        }

        if (reader.TokenType is not (JsonTokenType.StartObject or JsonTokenType.StartArray))
        {
            throw new JsonException($"Expected an object or an array of edits, got {reader.TokenType}.");
        }

        using JsonDocument top = JsonDocument.ParseValue(ref reader);
        return ReadElements(top.RootElement);
    }

    public override void Write(Utf8JsonWriter writer, List<FileEdit> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (FileEdit edit in value)
        {
            writer.WriteStartObject();
            writer.WriteString("oldString", edit.OldString);
            writer.WriteString("newString", edit.NewString);
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
    }

    private static List<FileEdit> ReadElements(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object)
        {
            // 形态二：漏了数组，单对象 → 包成单元素数组
            return [ReadEdit(root)];
        }

        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException(
                $"edits must be an array of {{oldString,newString}} objects or a single such object, "
                + $"got {root.ValueKind}.");
        }

        List<FileEdit> edits = new(root.GetArrayLength());
        foreach (JsonElement item in root.EnumerateArray())
        {
            edits.Add(ReadEdit(item));
        }

        return edits;
    }

    private static FileEdit ReadEdit(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException($"Each edit must be an object with oldString and newString, got {element.ValueKind}.");
        }

        return new FileEdit
        {
            // 形态三：属性名兼容 underscore 拼写
            OldString = GetString(element, "oldString") ?? GetString(element, "old_string") ?? string.Empty,
            NewString = GetString(element, "newString") ?? GetString(element, "new_string") ?? string.Empty,
        };
    }

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static string Preview(string s) => s.Length <= 40 ? s : s[..40] + "…";
}

/// <summary>
/// 工具调用参数的序列化口径。<b>基于框架默认那份复制</b>再加转换器——
/// 直接改 <see cref="AIJsonUtilities.DefaultOptions"/> 是全局改动，会波及所有工具与结构化输出。
/// </summary>
public static class ToolJson
{
    /// <summary>宽容口径：数组参数也接受标量字符串。其余行为与框架默认完全一致</summary>
    public static JsonSerializerOptions Lenient { get; } = CreateLenient();

    private static JsonSerializerOptions CreateLenient()
    {
        JsonSerializerOptions options = new(AIJsonUtilities.DefaultOptions);
        options.Converters.Add(new LenientStringArrayConverter());
        options.Converters.Add(new LenientFileEditListConverter());
        options.MakeReadOnly();
        return options;
    }
}
