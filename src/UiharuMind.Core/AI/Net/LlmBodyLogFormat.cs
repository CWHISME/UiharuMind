/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 请求与响应正文写进日志前的整理：抹掉 base64 载荷、展开成缩进 JSON、还原字符串里的换行
/// </summary>
internal static class LlmBodyLogFormat
{
    private const int Base64RedactThreshold = 512; //比这短的 base64 留着,可能是真内容而不是附件

    // data: URL 形式(MEAI 的 OpenAI 客户端就发这个),以及裸 base64 字符串值。
    // 正文里的自然语言必然带空格与标点,落不进 base64 字符集,因此这里不会误伤提示词
    private static readonly Regex DataUrlBase64 = new(
        $@"(data:[^"";\\]{{0,64}};base64,)[A-Za-z0-9+/=\s]{{{Base64RedactThreshold},}}",
        RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    private static readonly Regex BareBase64Value = new(
        $@"""[A-Za-z0-9+/=]{{{Base64RedactThreshold},}}""",
        RegexOptions.Compiled, TimeSpan.FromSeconds(2));

    //缩进 + 不转义非 ASCII。后者取代了原先那道 Regex.Unescape:
    //不加的话中文会写成 \uXXXX,日志基本没法读——这是编码器该干的事,不该靠事后拿正则去还原
    private static readonly JsonSerializerOptions LogJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>
    /// 把正文整理成可写进日志的形态：先抹掉 base64 载荷，再展开成缩进 JSON。
    ///
    /// 顺序不能反——先展开的话，那十几 MB 的 base64 会先被重新序列化一遍。
    /// 抹完之后正文通常只剩几 KB，展开的代价可以忽略。
    ///
    /// <b>不做任何长度截断</b>：磁盘上永不截断，截断只发生在面板的列表行。
    /// </summary>
    /// <param name="body">原始正文</param>
    /// <returns>可写进日志的文本</returns>
    internal static string ForLog(string body)
    {
        string text;
        try
        {
            text = DataUrlBase64.Replace(body, m => $"{m.Groups[1].Value}<{m.Length - m.Groups[1].Length} base64 chars>");
            text = BareBase64Value.Replace(text, m => $"\"<{m.Length - 2} base64 chars>\"");
        }
        catch (RegexMatchTimeoutException)
        {
            text = body; //抹不动就照原样,下面还有体量闸兜着
        }

        return Prettify(text);
    }

    // 不是 JSON 就原样返回:日志格式化失败不该影响任何事
    private static string Prettify(string text)
    {
        try
        {
            JsonNode? node = JsonNode.Parse(text);
            if (node == null) return text;
            return UnescapeJsonStringNewlines(node.ToJsonString(LogJsonOptions) ?? text);
        }
        catch (JsonException)
        {
            return text;
        }
    }

    // 字符串值里的换行被序列化器转义成字面 \n,日志里连成两行中间夹个反斜杠很难看。
    // WriteIndented 展开后,结构性的换行是<b>真换行</b>,字面 \n 只可能出现在字符串值内部,
    // 因此顺着 JSON 字符串扫描,把字符串值里的 \n 还原成真换行。不进字符串的结构换行不动。
    private static string UnescapeJsonStringNewlines(string pretty)
    {
        if (!pretty.Contains(@"\n", StringComparison.Ordinal)) return pretty;

        StringBuilder sb = new(pretty.Length);
        bool inString = false;
        for (int i = 0; i < pretty.Length; i++)
        {
            char c = pretty[i];

            if (inString)
            {
                if (c == '"')
                {
                    inString = false;
                    sb.Append(c);
                }
                else if (c == '\\' && i + 1 < pretty.Length && pretty[i + 1] == 'n')
                {
                    sb.Append('\n');
                    i++; //吞掉后面的 n
                }
                else
                {
                    sb.Append(c);
                }
            }
            else
            {
                if (c == '"') inString = true;
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
