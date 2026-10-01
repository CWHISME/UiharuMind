/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools;

/// <summary>
/// 工具输出超限时的<b>落盘 + 头尾骨架</b>：全文写到指定目录，返回给模型的是
/// <see cref="ToolOutputTruncation"/> 切好的头尾加一条带路径与续读提示的 Notice。
///
/// 目录由调用方给。MCP 结果落会话自己的产出房间（不是 Cache）：结果往往不可重现
/// （有副作用、数据会变），而历史里写着"全文在 X 路径"，清缓存就会留下坏链。
/// 网页则可以重抓，所以 WebFetch 仍走 <c>WebFetchCacheSink</c>。
/// </summary>
internal static class ToolResultSpill
{
    /// <summary>
    /// 限制一段文本的体量：不超预算原样返回；超了就落盘并返回头尾骨架。
    /// 落盘失败时退化为只截断（带上"未能保存"的说明），绝不因为写盘失败丢掉这次结果。
    /// </summary>
    /// <param name="text">工具输出全文</param>
    /// <param name="directory">落盘目录；不存在会创建</param>
    /// <param name="fileStem">文件名的可读前缀（如 <c>McpCall_github_search</c>）</param>
    /// <param name="budget">体量预算；缺省用 MCP 结果的紧凑档</param>
    /// <returns>给模型的最终文本</returns>
    public static string Limit(string text, string directory, string fileStem, ToolOutputBudget? budget = null)
    {
        ToolOutputBudget limits = budget ?? ToolOutputBudget.Compact;
        if (Encoding.UTF8.GetByteCount(text) <= limits.MaxBytes) return text;

        // 超限的 JSON 先缩进成多行：截断和续读都是按行的，而 MCP 结果常是一整行紧凑 JSON，
        // 单行时头尾重叠、尾部整个丢掉（日志类结果最新的恰恰在尾部），落盘文件也没法按行号续读
        text = IndentIfJson(text);

        try
        {
            string path = Save(text, directory, fileStem);
            return ToolOutputTruncation.Format(text, path, limits);
        }
        catch (Exception e)
        {
            Log.Warning($"Tool output spill failed ({fileStem}): {e.Message}");
            string head = ToolOutputTruncation.TakeHeadBytes(text, limits.HeadBytes);
            return $"{head}\n\n---\n*[Truncated — output exceeded {limits.MaxBytes} bytes " +
                   "and could not be saved to disk]*";
        }
    }

    /// <summary>
    /// 整段是合法 JSON 对象/数组时返回缩进后的文本；否则原样。语义等价，只是换行与空格。
    /// </summary>
    internal static string IndentIfJson(string text)
    {
        ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return text;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions
                   {
                       Indented = true,
                       Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                   }))
            {
                document.RootElement.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return text;
        }
    }

    /// 文件名 = 前缀 + 内容哈希：同一份输出重复调用落在同一文件，不同输出不会互相覆盖
    internal static string FileNameFor(string fileStem, string text) =>
        FileNameFor(fileStem, Encoding.UTF8.GetBytes(text), ".txt");

    /// <inheritdoc cref="FileNameFor(string, string)"/>
    internal static string FileNameFor(string fileStem, ReadOnlySpan<byte> content, string extension)
    {
        string prefix = new string(fileStem.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_')
            .Take(80).ToArray()).Trim('_');
        if (prefix.Length == 0) prefix = "output";
        string hash = Convert.ToHexStringLower(SHA256.HashData(content))[..8];
        return $"{prefix}_{hash}{extension}";
    }

    private static string Save(string text, string directory, string fileStem)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileNameFor(fileStem, text));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }
}
