namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Markdown 大纲：<c>#</c> 标题（ATX），深度 = 级数 - 1；围栏代码块里的 <c>#</c> 是注释不是标题，跳过。
/// 缩进代码块里的 <c>#</c> 同样跳过。下划线式标题（Setext）不认：少见，且要回看上一行。
/// 一节到下一个同级或更高级标题之前为止。
/// </summary>
internal sealed class MarkdownOutlineExtractor : IOutlineExtractor
{
    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".md", ".markdown"];

    /// <inheritdoc />
    public IEnumerable<OutlineEntry> Extract(IEnumerable<string> lines)
    {
        var entries = new List<OutlineEntry>();
        string? fence = null; //当前围栏的开头（``` 或 ~~~ 及其长度），null 表示不在代码块里
        int lineNumber = 0;
        foreach (string line in lines)
        {
            lineNumber++;
            if (fence == null && IsIndentedCode(line)) continue;

            string trimmed = line.TrimStart();
            string? marker = FenceMarker(trimmed);
            if (marker != null)
            {
                if (fence == null) fence = marker;
                else if (marker[0] == fence[0] && marker.Length >= fence.Length) fence = null;
                continue;
            }

            if (fence != null || !trimmed.StartsWith('#')) continue;

            int level = 0;
            while (level < trimmed.Length && trimmed[level] == '#') level++;
            if (level > 6 || (level < trimmed.Length && trimmed[level] != ' ')) continue; //#tag、####### 不是标题

            string title = trimmed[level..].Trim().TrimEnd('#').TrimEnd();
            if (title.Length == 0) continue;

            CloseSections(entries, level - 1, lineNumber - 1);
            entries.Add(new OutlineEntry(lineNumber, level - 1, $"{trimmed[..level]} {title}"));
        }

        CloseSections(entries, 0, lineNumber);
        return entries;
    }

    // 新标题出现时，同级及更深、还没结束的节都到上一行为止
    private static void CloseSections(List<OutlineEntry> entries, int depth, int endLine)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (entries[i].EndLine == 0 && entries[i].Depth >= depth) entries[i] = entries[i] with { EndLine = endLine };
        }
    }

    // 缩进 4 格（或制表符）起的是缩进代码块，里面的 # 是注释
    private static bool IsIndentedCode(string line) => line.StartsWith('\t') || line.StartsWith("    ");

    private static string? FenceMarker(string trimmed)
    {
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~')) return null;
        int length = 0;
        while (length < trimmed.Length && trimmed[length] == trimmed[0]) length++;
        return length >= 3 ? trimmed[..length] : null;
    }
}
