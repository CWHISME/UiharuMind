using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// Python 大纲：<c>class</c> 与 <c>def</c>（含 async），按缩进定层级与结束行。
/// 只列模块级与类体里的，函数里嵌套的 def 不列（口径同 C# 不列局部函数）。
/// 三引号字符串里的内容不认：docstring 里写 <c>def foo():</c> 示例很常见。
/// </summary>
internal sealed partial class PythonOutlineExtractor : IOutlineExtractor
{
    private const int MaxSignatureLines = 20; //签名跨行时最多往下攒这么多行，防括号不配平时吞掉整份文件
    private const int MaxTextChars = 120;

    /// <inheritdoc />
    public IReadOnlyList<string> Extensions { get; } = [".py", ".pyi"];

    /// <summary>一个打开中的 class/def 块</summary>
    /// <param name="Indent">块头的缩进</param>
    /// <param name="IsClass">是否 class（类体里的 def 才列）</param>
    /// <param name="Entry">对应的大纲条目下标，-1 表示不列</param>
    private readonly record struct Block(int Indent, bool IsClass, int Entry);

    /// <inheritdoc />
    public IEnumerable<OutlineEntry> Extract(IEnumerable<string> lines)
    {
        var entries = new List<OutlineEntry>();
        var blocks = new Stack<Block>();
        string? openQuote = null; //所在三引号字符串的引号，null 表示在代码里
        List<string>? signature = null; //攒到一半的跨行签名
        int signatureParens = 0; //签名里还没闭合的括号数
        int lastCodeLine = 0;
        int lineNumber = 0;

        foreach (string line in lines)
        {
            lineNumber++;
            bool startsInString = openQuote != null;
            int parens = ScanStrings(line, ref openQuote);

            if (signature != null)
            {
                signature.Add(line.Trim());
                lastCodeLine = lineNumber;
                signatureParens += parens;
                if (signatureParens <= 0 || signature.Count >= MaxSignatureLines) CloseSignature();
                continue;
            }

            // 三引号字符串里的行不看缩进（SQL 常顶格写），但仍属于所在的块
            if (startsInString)
            {
                lastCodeLine = lineNumber;
                continue;
            }

            string trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] == '#') continue;

            int indent = Indent(line);
            while (blocks.Count > 0 && blocks.Peek().Indent >= indent) CloseBlock(blocks.Pop());

            Match header = BlockHeader().Match(trimmed);
            lastCodeLine = lineNumber;
            if (!header.Success) continue;

            bool isClass = header.Groups["kind"].Value == "class";
            bool listed = blocks.Count == 0 || blocks.Peek().IsClass;
            int entry = -1;
            if (listed)
            {
                entries.Add(new OutlineEntry(lineNumber, blocks.Count, ""));
                entry = entries.Count - 1;
            }

            blocks.Push(new Block(indent, isClass, entry));
            signature = [trimmed];
            signatureParens = parens;
            if (signatureParens <= 0) CloseSignature();
        }

        if (signature != null) CloseSignature();
        while (blocks.Count > 0) CloseBlock(blocks.Pop());
        entries.RemoveAll(entry => entry.Text.Length == 0);
        return entries;

        void CloseSignature()
        {
            Block block = blocks.Peek();
            if (block.Entry >= 0) entries[block.Entry] = entries[block.Entry] with { Text = SignatureText(signature!) };
            signature = null;
        }

        void CloseBlock(Block block)
        {
            if (block.Entry >= 0) entries[block.Entry] = entries[block.Entry] with { EndLine = lastCodeLine };
        }
    }

    // 签名折成一行、去掉行尾冒号与注释
    private static string SignatureText(List<string> signature)
    {
        string text = Whitespace().Replace(string.Join(' ', signature.Select(StripComment)), " ").Trim()
            .Replace("( ", "(").Replace(" )", ")").TrimEnd(':').TrimEnd();
        return text.Length <= MaxTextChars ? text : text[..MaxTextChars] + "…";
    }

    private static string StripComment(string line)
    {
        int hash = line.IndexOf('#');
        return hash < 0 ? line : line[..hash];
    }

    private static int Indent(string line)
    {
        int width = 0;
        foreach (char c in line)
        {
            if (c == ' ') width++;
            else if (c == '\t') width += 8 - width % 8;
            else break;
        }

        return width;
    }

    /// <summary>
    /// 扫一行的字符串与注释：更新三引号状态，顺带数出字符串外的括号净增量（跨行签名用）
    /// </summary>
    private static int ScanStrings(string line, ref string? openQuote)
    {
        int parens = 0;
        int i = 0;
        while (i < line.Length)
        {
            if (openQuote != null)
            {
                int end = line.IndexOf(openQuote, i, StringComparison.Ordinal);
                if (end < 0) return parens;
                i = end + 3;
                openQuote = null;
                continue;
            }

            char c = line[i];
            if (c == '#') return parens;
            if (c is '(' or '[' or '{') parens++;
            else if (c is ')' or ']' or '}') parens--;

            if (c is not ('"' or '\''))
            {
                i++;
                continue;
            }

            if (i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)
            {
                openQuote = new string(c, 3);
                i += 3;
                continue;
            }

            // 单行字符串：跳到配对的引号
            i++;
            while (i < line.Length && line[i] != c) i += line[i] == '\\' ? 2 : 1;
            i++;
        }

        return parens;
    }

    [GeneratedRegex(@"^(?:async\s+)?(?<kind>def|class)\s+\w")]
    private static partial Regex BlockHeader();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
