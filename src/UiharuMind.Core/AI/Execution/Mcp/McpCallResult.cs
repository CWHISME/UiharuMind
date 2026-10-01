/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Files;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 把 MCP 工具调用的原始返回整形成给模型的结果：文本块受体量限制（超限落盘留头尾），
/// 图片、音频<b>落盘只给路径</b>——Chat Completions 的 tool 消息只收文本，非字符串结果会被整个序列化，
/// 留着 DataContent 等于把一串 base64 当文本塞给模型：看不到图，还白付 token。与 GenerateImage 同口径。
///
/// 原始返回的形态由框架的 MCP 包装决定（单块 → 该块；多块 → 块列表；
/// 出错或带结构化内容 → CallToolResult 的 JSON），这里对三种都要认。
///
/// <b>最后一种必须解包，不能原样交给模型。</b>整个 CallToolResult 序列化出来有两个毛病：
/// <c>content</c> 里的文本本身常是一段 JSON，套在 JSON 字符串里满是 <c>\u0022</c> 转义；
/// 而带结构化内容的 server 会把<b>同一份数据</b>在 <c>content</c> 与 <c>structuredContent</c> 里各放一遍
/// （MCP 规范要求这样做以兼容老客户端）。实测一次 73 个工具的清单 5974 字符，有用的只有约 2200。
/// 所以取 <c>content</c> 的文本；只有它是空的才退到 <c>structuredContent</c>。
/// </summary>
internal static class McpCallResult
{
    /// <summary>
    /// 整形一次调用的返回。
    /// </summary>
    /// <param name="raw">工具的原始返回</param>
    /// <param name="spillDirectory">超限落盘的目录</param>
    /// <param name="fileStem">落盘文件名的可读前缀</param>
    /// <param name="paths">落盘目录是会话房间时给出，图片路径按它写成草稿目录简写（与 GenerateImage 同口径）；null 写绝对路径</param>
    /// <returns>字符串；只有带认不出的非文本块时才是内容列表</returns>
    public static object Normalize(object? raw, string spillDirectory, string fileStem, AgentPathResolver? paths = null)
    {
        switch (raw)
        {
            case null:
                return "(empty result)";
            case string text:
                return ToolResultSpill.Limit(TidyJsonText(text), spillDirectory, fileStem);
            case TextContent textContent:
                return ToolResultSpill.Limit(TidyJsonText(textContent.Text), spillDirectory, fileStem);
            case JsonElement json:
                return NormalizeCallToolResult(json, spillDirectory, fileStem, paths);
            case AIContent other:
                return NormalizeContents([other], spillDirectory, fileStem, paths);
            case IEnumerable<AIContent> contents:
                return NormalizeContents(contents, spillDirectory, fileStem, paths);
            default:
                return ToolResultSpill.Limit(JsonSerializer.Serialize(raw), spillDirectory, fileStem);
        }
    }

    /// <summary>
    /// 解包序列化的 CallToolResult。认不出形状（没有 content 数组）就退回原始文本，不丢信息。
    /// </summary>
    internal static object NormalizeCallToolResult(JsonElement json, string spillDirectory, string fileStem,
        AgentPathResolver? paths = null)
    {
        if (json.ValueKind != JsonValueKind.Object ||
            !json.TryGetProperty("content", out JsonElement blocks) || blocks.ValueKind != JsonValueKind.Array)
        {
            return ToolResultSpill.Limit(json.GetRawText(), spillDirectory, fileStem);
        }

        List<AIContent> contents = new();
        foreach (JsonElement block in blocks.EnumerateArray())
        {
            AIContent? converted = ConvertBlock(block);
            if (converted != null) contents.Add(converted);
        }

        // content 里没有文本才退到 structuredContent，否则同一份数据会发两遍
        if (!contents.Any(x => x is TextContent) && json.TryGetProperty("structuredContent", out JsonElement structured)
                                                  && structured.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            contents.Add(new TextContent(structured.GetRawText()));
        }

        // 出错要让模型一眼看出来，否则一段报错文本读起来像正常结果
        if (json.TryGetProperty("isError", out JsonElement isError) && isError.ValueKind == JsonValueKind.True)
        {
            TextContent? first = contents.OfType<TextContent>().FirstOrDefault();
            if (first != null) first.Text = "Error: " + first.Text;
            else contents.Insert(0, new TextContent("Error: the tool reported a failure without details."));
        }

        return contents.Count == 0
            ? "(empty result)"
            : NormalizeContents(contents, spillDirectory, fileStem, paths);
    }

    /// <summary>
    /// 文本本身是一段带 <c>\uXXXX</c> 转义的 JSON 时，重新序列化成宽松转义的紧凑形态。
    ///
    /// 这种转义来自 server 自己的 JSON 编码器（.NET 默认会把引号写成 <c>\u0022</c>、<c>&gt;</c> 写成
    /// <c>\u003E</c>、中文写成 <c>\u4E2D</c>），不是我们包装出来的。语义完全等价，
    /// 但对模型是纯浪费：<c>\u0022</c> 六个字符对 <c>\"</c> 两个，中文更是一个字变六个字符。
    /// 实测一条 61743 字符的日志结果里满是这种转义。
    ///
    /// 只处理"整段文本是合法 JSON 对象/数组且含 <c>\u</c> 转义"的情形；解析失败一律原样返回，
    /// 绝不因为整理而丢掉或改坏 server 给的内容。
    /// </summary>
    /// <param name="text">工具返回的文本</param>
    /// <returns>整理后的文本；不适用则原样</returns>
    internal static string TidyJsonText(string text)
    {
        if (!text.Contains("\\u", StringComparison.Ordinal)) return text;

        ReadOnlySpan<char> trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length == 0 || (trimmed[0] != '{' && trimmed[0] != '[')) return text;

        try
        {
            using JsonDocument document = JsonDocument.Parse(text);
            using MemoryStream stream = new();
            using (Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Encoder = RelaxedEncoder }))
            {
                document.RootElement.WriteTo(writer);
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return text;
        }
    }

    private static readonly JavaScriptEncoder RelaxedEncoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping;

    /// 一个内容块转成 AIContent。认不出的类型保留原文，宁可丑一点也不吞信息
    private static AIContent? ConvertBlock(JsonElement block)
    {
        string type = block.TryGetProperty("type", out JsonElement t) ? t.GetString() ?? string.Empty : string.Empty;
        switch (type)
        {
            case "text":
                return block.TryGetProperty("text", out JsonElement text) ? new TextContent(text.GetString() ?? "") : null;
            case "image" or "audio":
                if (block.TryGetProperty("data", out JsonElement data) &&
                    block.TryGetProperty("mimeType", out JsonElement mime))
                {
                    try
                    {
                        return new DataContent(Convert.FromBase64String(data.GetString() ?? ""),
                            mime.GetString() ?? "application/octet-stream");
                    }
                    catch (FormatException)
                    {
                        // 不是合法 base64：原文就是那一大串，留着等于又塞给模型，只说一句
                        return new TextContent($"[{type} block with unreadable data omitted]");
                    }
                }

                return new TextContent(block.GetRawText());
            case "resource" when block.TryGetProperty("resource", out JsonElement resource)
                                 && resource.TryGetProperty("text", out JsonElement resourceText):
                return new TextContent(resourceText.GetString() ?? "");
            default:
                return new TextContent(block.GetRawText());
        }
    }

    private static object NormalizeContents(IEnumerable<AIContent> contents, string spillDirectory, string fileStem,
        AgentPathResolver? paths)
    {
        List<AIContent> all = contents.ToList();
        List<string> texts = all.OfType<TextContent>().Select(x => TidyJsonText(x.Text)).ToList();
        List<string> media = all.OfType<DataContent>().Select(x => SaveMedia(x, spillDirectory, fileStem, paths)).ToList();
        List<AIContent> others = all.Where(x => x is not (TextContent or DataContent)).ToList();

        List<string> lines = texts.Count > 0 ? [ToolResultSpill.Limit(string.Join("\n", texts), spillDirectory, fileStem)] : [];
        lines.AddRange(media);
        string joined = string.Join("\n", lines);
        if (others.Count == 0) return joined;

        List<AIContent> result = new();
        if (joined.Length > 0) result.Add(new TextContent(joined));
        result.AddRange(others);
        return result;
    }

    // 图片、音频落进产出房间的 images/（与 GenerateImage 同一处），结果里只留路径
    private static string SaveMedia(DataContent data, string spillDirectory, string fileStem, AgentPathResolver? paths)
    {
        string mediaType = data.MediaType;
        try
        {
            string directory = Path.Combine(spillDirectory, AgentOutputLayout.ImagesFolder);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory,
                ToolResultSpill.FileNameFor(fileStem, data.Data.Span, ExtensionOf(mediaType)));
            File.WriteAllBytes(path, data.Data.ToArray());
            return $"[{mediaType} saved: {paths?.ToPortable(path) ?? path}]";
        }
        catch (Exception e)
        {
            Log.Warning($"Mcp media save failed ({fileStem}): {e.Message}");
            return $"[{mediaType}, {data.Data.Length} bytes, could not be saved to disk]";
        }
    }

    private static string ExtensionOf(string mediaType)
    {
        if (mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return ImageFormats.ExtensionOf(mediaType);
        int slash = mediaType.IndexOf('/');
        string subtype = slash >= 0 ? mediaType[(slash + 1)..] : string.Empty;
        return subtype.Length > 0 && subtype.All(char.IsAsciiLetterOrDigit) ? "." + subtype.ToLowerInvariant() : ".bin";
    }
}

/// <summary>
/// 直挂 MCP 工具的结果整形层：让直挂与按需走同一套解包与超限落盘，
/// 而不是只有 <c>McpCall</c> 一条路受益。除结果外一律透传给原工具。
/// </summary>
internal sealed class McpResultFunction : DelegatingAIFunction
{
    private readonly string _spillDirectory;
    private readonly string _serverName;
    private readonly AgentPathResolver? _paths;

    /// <param name="innerFunction">原工具（可能已经是改过名的那个）</param>
    /// <param name="spillDirectory">超限落盘的目录</param>
    /// <param name="serverName">server 名，用作落盘文件名前缀的一部分</param>
    /// <param name="paths">落盘目录是会话房间时的路径口径；null 写绝对路径</param>
    public McpResultFunction(AIFunction innerFunction, string spillDirectory, string serverName,
        AgentPathResolver? paths = null)
        : base(innerFunction)
    {
        _spillDirectory = spillDirectory;
        _serverName = serverName;
        _paths = paths;
    }

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments,
        CancellationToken cancellationToken)
    {
        object? raw = await base.InvokeCoreAsync(arguments, cancellationToken).ConfigureAwait(false);
        return McpCallResult.Normalize(raw, _spillDirectory, $"Mcp_{_serverName}_{Name}", _paths);
    }
}
