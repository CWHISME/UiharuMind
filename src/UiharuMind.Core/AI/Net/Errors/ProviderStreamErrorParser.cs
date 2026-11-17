using System.Globalization;
using System.Text.Json;

namespace UiharuMind.Core.AI.Net.Errors;

/// <summary>
/// 从一条 SSE <c>data</c> 行里认出「上游错误帧」。
///
/// <para>
/// 形状有两种，都要认：<c>{"error":{"code":400,"message":"…"},"choices":[...]}</c>（实测 commandcode 网关）
/// 与 <c>{"choices":[{"delta":{},"finish_reason":"error"}]}</c>（只有收尾标记、没有错因）。
/// </para>
/// <para>
/// 先做一次子串预判再解析：这条流每帧都会走一遍，而工具参数那类长行动辄几百 KB，
/// 绝大多数帧里连 "error" 这个词都没有，先 IndexOf 一次比丢给 JSON 解析器便宜得多。
/// </para>
/// </summary>
internal static class ProviderStreamErrorParser
{
    /// <summary>
    /// 这条 <c>data</c> 行是不是错误帧
    /// </summary>
    /// <param name="line">整行原文（含 <c>data:</c> 前缀）</param>
    /// <param name="error">认出来时的异常对象</param>
    /// <returns>是错误帧返回 true</returns>
    public static bool TryParse(ReadOnlySpan<byte> line, out ProviderStreamErrorException? error)
    {
        error = null;
        if (!line.StartsWith("data:"u8)) return false;

        ReadOnlySpan<byte> payload = line[5..].TrimStart(" \t"u8);
        if (payload.IsEmpty || payload.SequenceEqual("[DONE]"u8)) return false;
        // 预判：没有 error 字段、也没有 finish_reason，就不必解析
        if (payload.IndexOf("\"error\""u8) < 0 && payload.IndexOf("\"finish_reason\""u8) < 0) return false;

        int? code = null;
        string? message = null;
        string? streamId = null;
        bool hasErrorObject = false;
        bool hasErrorCode = false;
        bool finishError = false;

        try
        {
            Utf8JsonReader reader = new(payload, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) return false;
            while (reader.Read())
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;

                if (reader.ValueTextEquals("id"))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String) streamId = reader.GetString();
                }
                else if (reader.ValueTextEquals("error"))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartObject)
                    {
                        ReadErrorBody(ref reader, ref code, ref message, ref hasErrorCode);
                        hasErrorObject = hasErrorCode || !string.IsNullOrWhiteSpace(message);
                    }
                    else if (reader.TokenType == JsonTokenType.String)
                    {
                        message = reader.GetString();
                        hasErrorObject = !string.IsNullOrWhiteSpace(message);
                    }
                    else if (reader.TokenType is not JsonTokenType.Null)
                    {
                        reader.Skip();
                    }
                }
                else if (reader.ValueTextEquals("choices"))
                {
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.StartArray) finishError = ScanChoices(ref reader);
                    else if (reader.TokenType is not JsonTokenType.Null) reader.Skip();
                }
                else
                {
                    reader.Skip();
                }
            }
        }
        catch (JsonException)
        {
            // 帧本身残缺（被截断的收尾帧）：不当错误帧，交给下游按原有口径处理
            return false;
        }

        if (!hasErrorObject && !finishError) return false;
        error = new ProviderStreamErrorException(code, message, streamId, finishReasonOnly: !hasErrorObject);
        return true;
    }

    /// <summary>读 error 对象里的 code / message；其余字段跳过</summary>
    private static void ReadErrorBody(ref Utf8JsonReader reader, ref int? code, ref string? message,
        ref bool hasErrorCode)
    {
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            if (reader.TokenType != JsonTokenType.PropertyName) continue;

            if (reader.ValueTextEquals("code"))
            {
                reader.Read();
                hasErrorCode = reader.TokenType == JsonTokenType.Number
                               || reader.TokenType == JsonTokenType.String
                               && !string.IsNullOrWhiteSpace(reader.GetString());
                code = ReadCode(ref reader);
            }
            else if (reader.ValueTextEquals("message"))
            {
                reader.Read();
                if (reader.TokenType == JsonTokenType.String) message = reader.GetString();
            }
            else
            {
                reader.Skip();
            }
        }
    }

    /// <summary>
    /// code 有时是数字、有时是字符串（商汤给 "9"），还有给 400.0 或对象形态的。
    /// 非标量要整块 Skip 掉：留在对象内部的话，外层 <c>ReadErrorBody</c> 会把这个对象的 EndObject
    /// 当成 error 对象的收尾，后面的 message 一个都读不到
    /// </summary>
    private static int? ReadCode(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out int numeric)) return numeric;
                if (reader.TryGetDouble(out double real) && real is >= int.MinValue and <= int.MaxValue)
                    return (int)real;
                return null;
            case JsonTokenType.String:
                string? text = reader.GetString();
                if (int.TryParse(text, out int parsed)) return parsed;
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double fromText)
                    && fromText is >= int.MinValue and <= int.MaxValue)
                    return (int)fromText;
                return null;
            default:
                reader.Skip();
                return null;
        }
    }

    /// <summary>choices 数组里有没有 finish_reason=error</summary>
    private static bool ScanChoices(ref Utf8JsonReader reader)
    {
        bool found = false;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                reader.Skip();
                continue;
            }

            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                if (!reader.ValueTextEquals("finish_reason"))
                {
                    reader.Skip();
                    continue;
                }

                reader.Read();
                if (reader.TokenType == JsonTokenType.String && reader.ValueTextEquals("error"u8)) found = true;
            }
        }

        return found;
    }
}
