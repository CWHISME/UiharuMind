using System.Buffers;

namespace UiharuMind.Core.AI.Reasoning;

/// <summary>
/// 从响应字节里把 <c>reasoning_details</c> 那一整个数组<b>原样</b>切下来。
///
/// 为什么只切不解析：里面是密文/签名，任何再编码（解析后重排、解码再编）都会让它作废。
/// 客户端唯一该做的是保管它、原样出示给产出它的那个模型——所以拿到的字节直接拼回请求体。
///
/// 只认紧凑写法：SSE 帧里的 JSON 必然是紧凑的（换行会截断帧），不存在 <c>"reasoning_details": [</c>。
/// </summary>
internal static class ReasoningDetailExtractor
{
    private static readonly byte[] Key = "\"reasoning_details\":"u8.ToArray();

    /// <summary>
    /// 在一行/一段 JSON 里找 <c>"reasoning_details"</c> 的值，切出它的原始字节。
    /// </summary>
    /// <param name="json">待扫描的字节（SSE 的一行，或一次性响应的整个正文）</param>
    /// <param name="value">切出来的数组原文（含方括号）；数组为空时为空数组</param>
    /// <returns>找到了则 true</returns>
    public static bool TryExtract(ReadOnlySpan<byte> json, out byte[] value)
    {
        value = [];
        int at = json.IndexOf(Key);
        if (at < 0) return false;

        ReadOnlySpan<byte> rest = json[(at + Key.Length)..];
        int start = rest.IndexOf((byte)'[');
        if (start < 0) return false;

        int depth = 0;
        bool inString = false;
        for (int i = start; i < rest.Length; i++)
        {
            byte current = rest[i];
            if (inString)
            {
                if (current == (byte)'\\') i++; //跳过转义的下一个字节
                else if (current == (byte)'"') inString = false;
                continue;
            }

            switch (current)
            {
                case (byte)'"':
                    inString = true;
                    break;
                case (byte)'[':
                    depth++;
                    break;
                case (byte)']':
                    if (--depth == 0)
                    {
                        value = rest.Slice(start, i - start + 1).ToArray();
                        return true;
                    }

                    break;
            }
        }

        return false; //半截数组(帧被切断)当没找到:残缺的票发回去比不发更糟
    }

    /// <summary>
    /// 从切出来的数组里读明文摘要（<c>reasoning.summary</c> 条目的 <c>summary</c>）。
    ///
    /// 只有摘要能显示给用户：加密条目（<c>reasoning.encrypted</c>）没有明文，
    /// 编不出来也不该编。摘要<b>不进回填</b>——服务端不认摘要回传，带回去是白改请求体。
    /// </summary>
    /// <param name="details">切出来的数组原文</param>
    /// <returns>摘要正文；没有摘要条目时为 null</returns>
    public static string? ReadSummary(byte[] details)
    {
        // 明文摘要的条目形如 {"type":"reasoning.summary","summary":"..."}
        // 只取文字，不解码其余字段——密文那条的 data 不能碰
        ReadOnlySpan<byte> text = details;
        int at = text.IndexOf("\"reasoning.summary\""u8);
        if (at < 0) return null;

        ReadOnlySpan<byte> rest = text[(at + "\"reasoning.summary\"".Length)..];
        int key = rest.IndexOf("\"summary\""u8);
        if (key < 0) return null;

        rest = rest[(key + "\"summary\"".Length)..];
        int colon = rest.IndexOf((byte)':');
        if (colon < 0) return null;

        rest = rest[(colon + 1)..];
        while (rest.Length > 0 && (rest[0] == (byte)' ' || rest[0] == (byte)'\t')) rest = rest[1..];
        if (rest.Length == 0 || rest[0] != (byte)'"') return null;

        int end = 1;
        while (end < rest.Length)
        {
            if (rest[end] == (byte)'\\')
            {
                end += 2;
                continue;
            }

            if (rest[end] == (byte)'"') break;
            end++;
        }

        if (end >= rest.Length) return null;
        try
        {
            return System.Text.Encoding.UTF8.GetString(rest.Slice(1, end - 1));
        }
        catch (System.Text.DecoderFallbackException)
        {
            return null;
        }
    }
}
