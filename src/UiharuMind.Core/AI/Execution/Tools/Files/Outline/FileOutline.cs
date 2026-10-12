using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 文件大纲：Read 截断时附在正文后，让模型看清整份文件的结构、按行号 offset 跳读，
/// 而不是一页页往下翻。确定性、不调模型——与 Grep 命中地图「给分布，不给半截」同一思路。
///
/// 按扩展名分派给 <see cref="IOutlineExtractor"/>，预算与渲染在这里统一：
/// 条目超上限时先整层裁掉最深的结构（保住骨架），仍超才截尾。
/// 新增文件类型只需实现抽取器并加进 <see cref="Extractors"/>。
/// </summary>
internal static class FileOutline
{
    /// <summary>Read 截断时附带的大纲条目上限：一行约 15 token，60 行约 1k token</summary>
    internal const int MaxEntries = 60;

    /// <summary>模型主动要大纲时的条目上限：它就是来看结构的，给宽些</summary>
    internal const int MaxRequestedEntries = 200;

    private const int MaxLookupBytes = 1024 * 1024; //Grep 标注所在结构时读的文件上限，再大多是生成物

    private static readonly IOutlineExtractor[] Extractors =
    [
        new CSharpOutlineExtractor(),
        new CppOutlineExtractor(),
        new JavaOutlineExtractor(),
        new TypeScriptOutlineExtractor(),
        new GoOutlineExtractor(),
        new RustOutlineExtractor(),
        new PythonOutlineExtractor(),
        new MarkdownOutlineExtractor(),
    ];

    private static readonly Dictionary<string, IOutlineExtractor> ByExtension = Extractors
        .SelectMany(extractor => extractor.Extensions.Select(extension => (extension, extractor)))
        .ToDictionary(x => x.extension, x => x.extractor, StringComparer.OrdinalIgnoreCase);

    /// <summary>支持的扩展名，给模型的提示用</summary>
    internal static string SupportedExtensions { get; } = string.Join(" ", ByExtension.Keys.Order(StringComparer.Ordinal));

    /// <summary>
    /// 是否支持这类文件
    /// </summary>
    /// <param name="filePath">文件路径（只看扩展名）</param>
    /// <returns>是否有对应的抽取器</returns>
    internal static bool Supports(string filePath) => ByExtension.ContainsKey(Path.GetExtension(filePath));

    /// <summary>
    /// 抽取大纲条目
    /// </summary>
    /// <param name="filePath">文件路径（只看扩展名）</param>
    /// <param name="lines">文件的全部行</param>
    /// <returns>条目，行号升序；不支持的类型为 null</returns>
    internal static List<OutlineEntry>? Extract(string filePath, IEnumerable<string> lines) =>
        ByExtension.TryGetValue(Path.GetExtension(filePath), out IOutlineExtractor? extractor)
            ? extractor.Extract(lines).ToList()
            : null;

    /// <summary>
    /// 从磁盘读文件并抽取大纲条目，给 Grep 标注命中所在的结构用。只读小文本文件，读不了一律返回 null
    /// </summary>
    /// <param name="fullPath">文件绝对路径</param>
    /// <returns>条目；不支持、过大、二进制或读失败时为 null</returns>
    internal static List<OutlineEntry>? ExtractFile(string fullPath)
    {
        if (!Supports(fullPath)) return null;
        try
        {
            var info = new FileInfo(fullPath);
            if (!info.Exists || info.Length > MaxLookupBytes) return null;

            byte[] bytes = File.ReadAllBytes(fullPath);
            TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);
            return probe.Verdict == ETextEncodingVerdict.Binary ? null : Extract(fullPath, ReadLines(probe.Decode(bytes)));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// 某行所在的最内层结构
    /// </summary>
    /// <param name="entries">大纲条目，行号升序</param>
    /// <param name="line">行号，1 起算</param>
    /// <param name="excludeStartingHere">不算从这一行开始的结构（该行就是它的声明，要的是外层）</param>
    /// <returns>包含该行的最深条目；不在任何结构里为 null</returns>
    internal static OutlineEntry? Enclosing(IReadOnlyList<OutlineEntry> entries, int line, bool excludeStartingHere = false)
    {
        // 嵌套的结构起始更晚，行号升序里最后一个包含它的就是最内层
        OutlineEntry? found = null;
        foreach (OutlineEntry entry in entries)
        {
            if (entry.Line > line || (excludeStartingHere && entry.Line == line)) break;
            if (entry.Contains(line)) found = entry;
        }

        return found;
    }

    /// <summary>
    /// 渲染文件大纲
    /// </summary>
    /// <param name="filePath">文件路径（只看扩展名）</param>
    /// <param name="lines">文件的全部行</param>
    /// <param name="maxEntries">条目上限</param>
    /// <param name="shownThrough">正文已显示到第几行：整段落在其中的结构模型已经看到，不再列出；0 表示没显示正文</param>
    /// <returns>大纲文本；不支持的类型或抽不出结构时为 null</returns>
    internal static string? Render(string filePath, IEnumerable<string> lines, int maxEntries = MaxEntries,
        int shownThrough = 0)
    {
        List<OutlineEntry>? entries = Extract(filePath, lines);
        if (entries is not { Count: > 0 }) return null;

        entries.RemoveAll(entry => entry.EndLine > 0 && entry.EndLine <= shownThrough);
        if (entries.Count == 0) return null;

        // 最浅一层归零：Markdown 从 ## 起写的文件不该整体缩进一格
        int minDepth = entries.Min(entry => entry.Depth);
        int maxDepth = entries.Max(entry => entry.Depth);

        // 超预算先整层裁掉最深的结构（保住骨架）。顶层只有一个结构（一个文件一个类）时，
        // 骨架是它下面那一层，裁到只剩它自己等于什么都没给
        int floorDepth = maxDepth > minDepth && entries.Count(entry => entry.Depth == minDepth) == 1 ? minDepth + 1 : minDepth;
        bool pruned = false;
        while (entries.Count > maxEntries && maxDepth > floorDepth)
        {
            int deepest = maxDepth;
            entries.RemoveAll(entry => entry.Depth == deepest);
            maxDepth--;
            pruned = true;
        }

        // 带上止行：模型能算出 head，只读这一段。行号套方括号，不与 Grep 的「行号:」「行号-」混淆
        var sb = new StringBuilder("文件大纲（[起–止行] 结构，可传 offset 与 head 只读对应的段）：");
        if (shownThrough > 0) sb.Append("\n（上面已显示的第 1–").Append(shownThrough).Append(" 行里的结构不再列出）");
        foreach (OutlineEntry entry in entries.Take(maxEntries))
        {
            sb.Append("\n[").Append(FormatRange(entry)).Append("] ")
              .Append(' ', (entry.Depth - minDepth) * 2).Append(entry.Text);
        }

        bool truncated = entries.Count > maxEntries;
        if (truncated) sb.Append("\n…另有 ").Append(entries.Count - maxEntries).Append(" 项未列出");
        if (pruned) sb.Append("\n（结构较多，更深的层级未列出）");
        if ((truncated || pruned) && maxEntries < MaxRequestedEntries) sb.Append("\n要看完整大纲传 outline=true。");
        return sb.ToString();
    }

    /// <summary>
    /// 条目的行号范围：多行为 <c>起–止</c>（与 Read 截断附注同用 en dash），单行只写一个行号
    /// </summary>
    /// <param name="entry">大纲条目</param>
    /// <returns>范围文本</returns>
    internal static string FormatRange(OutlineEntry entry) =>
        entry.EndLine > entry.Line ? $"{entry.Line}–{entry.EndLine}" : entry.Line.ToString();

    // 切行口径同 StreamReader.ReadLine，与 Read 的行号对得上
    private static IEnumerable<string> ReadLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line) yield return line;
    }
}
