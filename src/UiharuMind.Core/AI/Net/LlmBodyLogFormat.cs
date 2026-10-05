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
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 请求与响应正文写进日志前的整理：抹掉 base64 载荷、展开成两格缩进的 JSON、字符串里的换行还原成真换行。
///
/// 吃 UTF-8 字节、写进池化缓冲，一遍 <see cref="Utf8JsonReader"/> 自己排版，不建 DOM、不经字符串——
/// 请求体动辄几十万字，DOM、缩进串与事后还原换行各一份都在大对象堆上。
/// 不用 <see cref="Utf8JsonWriter"/>：它的 <c>WriteRawValue</c> 不缩进，字符串转义也没法留下真换行。
///
/// <b>不做任何长度截断</b>：磁盘上永不截断，截断只发生在面板的列表行。
/// </summary>
internal static class LlmBodyLogFormat
{
    private const int Base64RedactThreshold = 512; //比这短的 base64 留着,可能是真内容而不是附件
    private const int MaxDataUrlPrefix = 69; //"data:" + 至多 64 字符的媒体类型

    // 宽松编码器:中文原样。默认编码器会写成 \uXXXX,日志基本没法读
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly SearchValues<byte> Base64Bytes =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="u8);

    /// <summary>
    /// 把正文整理成可写进日志的形态。base64 在解码后的字符串值上判，抹掉的载荷换成一句体量说明：
    /// 那不是截断，是把毫无阅读价值的附件（一张图就十几 MB）换掉。不是 JSON 就原样照抄
    /// </summary>
    /// <param name="body">UTF-8 正文</param>
    /// <param name="output">写入整理后的正文</param>
    internal static void Format(ReadOnlySpan<byte> body, PooledByteWriter output)
    {
        int mark = output.WrittenCount;
        byte[] scratch = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            WriteIndented(body, output, ref scratch);
        }
        catch (JsonException)
        {
            output.Rewind(mark); //日志格式化失败不该影响任何事
            output.Write(body);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
        }
    }

    private static void WriteIndented(ReadOnlySpan<byte> body, PooledByteWriter output, ref byte[] scratch)
    {
        Span<bool> hasItems = stackalloc bool[65]; //读器默认最多 64 层,再深会抛 JsonException
        int depth = 0;
        bool afterName = false;
        var reader = new Utf8JsonReader(body);
        while (reader.Read())
        {
            JsonTokenType token = reader.TokenType;
            if (token is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                if (hasItems[depth]) NewLine(output, depth - 1);
                depth--;
                output.Write(token == JsonTokenType.EndObject ? "}"u8 : "]"u8);
                continue;
            }

            // 每个键或值之前：逗号 + 换行缩进（紧跟在键后的值除外）
            if (afterName)
            {
                afterName = false;
            }
            else if (depth > 0)
            {
                if (hasItems[depth]) output.Write(","u8);
                NewLine(output, depth);
                hasItems[depth] = true;
            }

            switch (token)
            {
                case JsonTokenType.StartObject:
                case JsonTokenType.StartArray:
                    output.Write(token == JsonTokenType.StartObject ? "{"u8 : "["u8);
                    hasItems[++depth] = false;
                    break;
                case JsonTokenType.PropertyName:
                    WriteQuoted(output, Unescaped(ref reader, ref scratch));
                    output.Write(": "u8);
                    afterName = true;
                    break;
                case JsonTokenType.String:
                    WriteString(output, Unescaped(ref reader, ref scratch));
                    break;
                default:
                    output.Write(reader.ValueSpan); //数字与 true/false/null 原样
                    break;
            }
        }
    }

    private static void NewLine(PooledByteWriter output, int depth)
    {
        Span<byte> span = output.GetSpan(1 + depth * 2);
        span[0] = (byte)'\n';
        span.Slice(1, depth * 2).Fill((byte)' ');
        output.Advance(1 + depth * 2);
    }

    private static ReadOnlySpan<byte> Unescaped(ref Utf8JsonReader reader, ref byte[] scratch)
    {
        if (!reader.ValueIsEscaped) return reader.ValueSpan;
        if (scratch.Length < reader.ValueSpan.Length)
        {
            ArrayPool<byte>.Shared.Return(scratch);
            scratch = ArrayPool<byte>.Shared.Rent(reader.ValueSpan.Length);
        }

        return scratch.AsSpan(0, reader.CopyString(scratch));
    }

    // 按宽松编码器转义，但真换行原样留下：转义过的反斜杠在这之前已被解码，"C:\\new" 不会被当成换行
    private static void WriteQuoted(PooledByteWriter output, ReadOnlySpan<byte> value)
    {
        output.Write("\""u8);
        while (true)
        {
            int newline = value.IndexOf((byte)'\n');
            ReadOnlySpan<byte> part = newline < 0 ? value : value[..newline];
            while (part.Length > 0)
            {
                Span<byte> destination = output.GetSpan(part.Length * 6 + 16);
                Encoder.EncodeUtf8(part, destination, out int consumed, out int written);
                output.Advance(written);
                part = part[consumed..];
            }

            if (newline < 0) break;
            output.Write("\n"u8);
            value = value[(newline + 1)..];
        }

        output.Write("\""u8);
    }

    // data:...;base64,<载荷> 与裸 base64 串：载荷够长时换成一句体量说明
    private static void WriteString(PooledByteWriter output, ReadOnlySpan<byte> value)
    {
        if (value.Length >= Base64RedactThreshold)
        {
            Span<byte> note = stackalloc byte[MaxDataUrlPrefix + 96];
            int marker = value.IndexOf(";base64,"u8);
            if (marker >= 0 && marker <= MaxDataUrlPrefix && value.StartsWith("data:"u8))
            {
                int payloadStart = marker + ";base64,"u8.Length;
                ReadOnlySpan<byte> payload = value[payloadStart..];
                if (payload.Length >= Base64RedactThreshold && payload.IndexOfAnyExcept(Base64Bytes) < 0)
                {
                    value[..payloadStart].CopyTo(note);
                    WriteQuoted(output, note[..(payloadStart + Placeholder(payload.Length, note[payloadStart..]))]);
                    return;
                }
            }
            else if (value.IndexOfAnyExcept(Base64Bytes) < 0)
            {
                WriteQuoted(output, note[..Placeholder(value.Length, note)]);
                return;
            }
        }

        WriteQuoted(output, value);
    }

    private static int Placeholder(int length, Span<byte> destination)
    {
        destination[0] = (byte)'<';
        length.TryFormat(destination[1..], out int digits);
        " base64 chars>"u8.CopyTo(destination[(1 + digits)..]);
        return 1 + digits + " base64 chars>"u8.Length;
    }
}
