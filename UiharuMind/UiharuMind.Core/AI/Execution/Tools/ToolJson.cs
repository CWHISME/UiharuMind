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
        options.MakeReadOnly();
        return options;
    }
}
