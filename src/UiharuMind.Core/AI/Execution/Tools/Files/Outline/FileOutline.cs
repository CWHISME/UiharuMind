using System.Text;

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
    private const int MaxEntries = 60; //一行约 15 token，60 行约 1k token

    private static readonly IOutlineExtractor[] Extractors =
    [
        new CSharpOutlineExtractor(),
        new MarkdownOutlineExtractor(),
    ];

    private static readonly Dictionary<string, IOutlineExtractor> ByExtension = Extractors
        .SelectMany(extractor => extractor.Extensions.Select(extension => (extension, extractor)))
        .ToDictionary(x => x.extension, x => x.extractor, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 渲染文件大纲
    /// </summary>
    /// <param name="filePath">文件路径（只看扩展名）</param>
    /// <param name="lines">文件的全部行</param>
    /// <returns>大纲文本；不支持的类型或抽不出结构时为 null</returns>
    internal static string? Render(string filePath, IEnumerable<string> lines)
    {
        if (!ByExtension.TryGetValue(Path.GetExtension(filePath), out IOutlineExtractor? extractor)) return null;

        List<OutlineEntry> entries = extractor.Extract(lines).ToList();
        if (entries.Count == 0) return null;

        // 最浅一层归零：Markdown 从 ## 起写的文件不该整体缩进一格
        int minDepth = entries.Min(entry => entry.Depth);
        int maxDepth = entries.Max(entry => entry.Depth);
        bool pruned = false;
        while (entries.Count > MaxEntries && maxDepth > minDepth)
        {
            int deepest = maxDepth;
            entries.RemoveAll(entry => entry.Depth == deepest);
            maxDepth--;
            pruned = true;
        }

        var sb = new StringBuilder("文件大纲（行号: 结构，可传 offset 跳到对应行）：");
        foreach (OutlineEntry entry in entries.Take(MaxEntries))
        {
            sb.Append('\n').Append(entry.Line).Append(": ")
              .Append(' ', (entry.Depth - minDepth) * 2).Append(entry.Text);
        }

        if (entries.Count > MaxEntries) sb.Append("\n…另有 ").Append(entries.Count - MaxEntries).Append(" 项未列出");
        if (pruned) sb.Append("\n（结构较多，更深的层级未列出）");
        return sb.ToString();
    }
}
