using System.Buffers;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace HttpAllocBench.Proto;

/// <summary>
/// P2 原型：与 LlmBodyLogFormat.ForLog 同目标（抹 base64、两格缩进、不转义中文、字符串里的 \n 还原成真换行），
/// 但直接吃 UTF-8 字节、写进池化缓冲：一遍 Utf8JsonReader，自己排版（Utf8JsonWriter 的 WriteRawValue 不缩进数字），
/// 换行在转义时就地还原，不再事后扫第二遍。
/// base64 在解码后的字符串上判，不受原文转义写法影响。
/// </summary>
internal static class Utf8BodyLogFormat
{
    private const int Base64RedactThreshold = 512;
    private static readonly JavaScriptEncoder Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    private static readonly SearchValues<byte> Base64Bytes =
        SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/="u8);

    /// <returns>写进 output 的字节数；不是合法 JSON 时返回 -1（调用方照原样记）</returns>
    public static int Format(ReadOnlySpan<byte> json, PooledByteWriter output)
    {
        int mark = output.WrittenCount;
        byte[] scratch = ArrayPool<byte>.Shared.Rent(4096);
        Span<bool> hasItems = stackalloc bool[65];
        int depth = 0;
        bool afterName = false;
        try
        {
            var reader = new Utf8JsonReader(json);
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

                // 每个值或键之前：逗号 + 换行缩进（紧跟在键后的值除外）
                if (afterName) afterName = false;
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

            return output.WrittenCount - mark;
        }
        catch (JsonException)
        {
            output.Rewind(mark);
            return -1;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(scratch);
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

        int length = reader.CopyString(scratch);
        return scratch.AsSpan(0, length);
    }

    // 按宽松编码器转义，但真换行原样留下（日志里就是要它换行）
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
                Encoder.EncodeUtf8(part, destination, out int consumed, out int written, isFinalBlock: true);
                output.Advance(written);
                part = part[consumed..];
            }

            if (newline < 0) break;
            output.Write("\n"u8);
            value = value[(newline + 1)..];
        }

        output.Write("\""u8);
    }

    // data:...;base64,<载荷> 与裸 base64 串：载荷 ≥ 512 时换成一句体量说明
    private static void WriteString(PooledByteWriter output, ReadOnlySpan<byte> value)
    {
        if (value.Length >= Base64RedactThreshold)
        {
            Span<byte> note = stackalloc byte[160];
            int marker = value.IndexOf(";base64,"u8);
            if (marker >= 0 && marker <= 69 && value.StartsWith("data:"u8))
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
        int written = 0;
        destination[written++] = (byte)'<';
        length.TryFormat(destination[written..], out int digits);
        written += digits;
        " base64 chars>"u8.CopyTo(destination[written..]);
        return written + " base64 chars>"u8.Length;
    }
}
