using System.Buffers;

namespace UiharuMind.Core.AI.Net.Response;

/// <summary>
/// 从响应字节里把 <c>reasoning_details</c> 那一整个数组<b>原样</b>切下来。
///
/// 为什么只切不解析：它是签名/密文，任何再编码（解析后重排、解码再编）都会让它作废。
/// 客户端唯一该做的是保管它、原样出示给产出它的那个模型——所以这里拿到的字节会直接拼回请求体。
///
/// 只认紧凑写法：SSE 帧里的 JSON 必然是紧凑的（换行会截断帧），不存在 <c>"reasoning_details": [</c>。
/// </summary>
internal static class ReasoningDetailsGrabber
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

        return false; //半截数组(帧被切断)当没找到:残缺的票据发回去比不发更糟
    }
}
