using System.Text;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>Read 读到图片时怎么办。随装配定：话里指名的工具必须真的挂着</summary>
public enum EReadImageMode
{
    /// <summary>这次装配看不了图，如实说</summary>
    None,

    /// <summary>视觉模型：附图给它自己看（ADR 0053）</summary>
    Attach,

    /// <summary>非视觉模型：指向 AnalyzeImage</summary>
    PointToAnalyzeImage,
}

/// <summary>
/// Read 工具的实现。按参数里的扩展名分流：图片按 <see cref="EReadImageMode"/> 处理，其余按探测出的编码读成一段文本。
/// 编码与知识库、Edit/Write 同一份口径（<see cref="TextEncodingDetector"/>），于是 Read 给模型看的，
/// 就是 Edit 拿去匹配、写回时还原的那份正文。
///
/// 附注只在需要时出现，统一贴在正文之后：截断时给行号范围、总行数与续读的 offset；非 UTF-8 时注明编码。
/// 读不了时说清是目录、空文件、越界还是二进制；找不到时顺手列出工作区里的同名文件——
/// 实测找不到多半是目录猜错了，这样省掉模型再跑一次 Glob。
/// </summary>
internal sealed class FileReader
{
    /// <summary>未显式传 limit 时的行数上限</summary>
    internal const int DefaultLineLimit = 2000;

    /// <summary>
    /// 单次返回的总量上限，按 <b>UTF-8 字节</b>算而不是字符：中文约 1~1.5 字符/token，
    /// 按字符算实际能放进标称值三四倍的 token。与 Grep 的正文预算同口径；要全文走 <c>limit=-1</c>。
    /// </summary>
    internal const int MaxTotalBytes = 32 * 1024;

    /// <summary>单行截断（压缩产物一行可达数百 KB）</summary>
    internal const int MaxLineChars = 2000;

    private const long WholeFileBytes = 8 * 1024 * 1024; //这么大以内整份读进来：编码看全文，顺手数出总行数
    private const int SampleBytes = 64 * 1024; //更大的文件只拿开头这么多探测编码
    private const int MaxSameNameFiles = 5;
    private static readonly TimeSpan SameNameSearchBudget = TimeSpan.FromSeconds(2); //找同名只是顺手，大工作区不能拖住这次调用

    private readonly AgentPathResolver _paths;
    private readonly SimpleGlobber _glob;
    private readonly EReadImageMode _imageMode;
    private readonly ImagePreview.ImageDownscaler? _downscaler;

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="paths">路径口径</param>
    /// <param name="imageMode">读到图片时怎么办</param>
    /// <param name="downscaler">看图的缩图器；null 原样复制</param>
    public FileReader(AgentPathResolver paths, EReadImageMode imageMode, ImagePreview.ImageDownscaler? downscaler)
    {
        _paths = paths;
        _glob = new SimpleGlobber(paths);
        _imageMode = imageMode;
        _downscaler = downscaler;
    }

    /// <summary>
    /// 读一个文件
    /// </summary>
    /// <param name="filePath">模型给的路径</param>
    /// <param name="offset">起始行（从 1 起）；负数从文件末尾倒数</param>
    /// <param name="limit">最多几行；-1 读全文且不设字节上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>工具结果</returns>
    public async Task<string> ReadAsync(string filePath, int offset, int? limit, CancellationToken ct)
    {
        if (!_paths.TryResolve(filePath, out string full, out string pathError)) return pathError;
        if (_paths.RelativeToMemory(full) == ".") return ReadMemoryIndex();
        if (Directory.Exists(full)) return $"'{filePath}' is a directory. Use `{FileToolNames.Glob}` to list its files.";
        if (!File.Exists(full)) return await NotFoundAsync(filePath, full, ct).ConfigureAwait(false);

        // 只按扩展名分流：投影也只认参数里的扩展名，两边口径一致
        if (ImagePreview.IsImagePath(filePath)) return await ReadImageAsync(filePath, full, ct).ConfigureAwait(false);

        long size = new FileInfo(full).Length;
        return size <= WholeFileBytes
            ? await ReadWholeAsync(filePath, full, offset, limit, ct).ConfigureAwait(false)
            : ReadStreaming(filePath, full, size, offset, limit);
    }

    // 读记忆目录本身 = 读记忆索引:每份笔记一行「文件名 — 首行摘要」
    private string ReadMemoryIndex() =>
        MemoryIndex.Build(_paths.MemoryRoot, string.Empty, AgentSettingConfig.Current.MemoryIndexBudget)
        ?? "The memory directory has no notes yet.";

    private Task<string> ReadImageAsync(string filePath, string full, CancellationToken ct) => _imageMode switch
    {
        EReadImageMode.Attach => ImagePreview.AttachAsync(_paths, filePath, full, _downscaler, ct),
        EReadImageMode.PointToAnalyzeImage => ImagePreview.DescribeAsync(filePath, full,
            $"You can't see images yourself; ask `{VisionTool.ToolName}` about it.", ct),
        _ => ImagePreview.DescribeAsync(filePath, full, "Images can't be viewed in this session.", ct),
    };

    private static async Task<string> ReadWholeAsync(string filePath, string full, int offset, int? limit,
        CancellationToken ct)
    {
        byte[] bytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
        TextEncodingProbe probe = TextEncodingDetector.Detect(bytes);
        if (probe.Verdict == ETextEncodingVerdict.Binary) return BinaryNotice(filePath, bytes.Length);

        string text = probe.Decode(bytes);
        List<Range> lines = SplitLines(text);
        if (lines.Count == 0) return $"File '{filePath}' is empty.";

        int start = StartLine(offset, lines.Count);
        if (start > lines.Count) return $"offset {offset} is past the end of '{filePath}' ({lines.Count} lines).";

        (List<string> window, bool hasMore) = TakeWindow(lines.Skip(start - 1).Select(range => text[range]), limit);
        return Render(window, start, hasMore, lines.Count.ToString(), EncodingNote(probe));
    }

    // 大文件不整份进内存：编码只看开头一段，总行数只在倒数读时才数（要多扫一遍）
    private static string ReadStreaming(string filePath, string full, long size, int offset, int? limit)
    {
        byte[] head = new byte[SampleBytes];
        int headLength;
        using (FileStream stream = File.OpenRead(full))
        {
            headLength = stream.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        }

        TextEncodingProbe probe = TextEncodingDetector.Detect(head.AsSpan(0, headLength), isComplete: false);
        if (probe.Verdict == ETextEncodingVerdict.Binary) return BinaryNotice(filePath, size);

        int? total = offset < 0 ? ReadLines(full, probe).Count() : null;
        int start = StartLine(offset, total ?? 0);
        (List<string> window, bool hasMore) = TakeWindow(ReadLines(full, probe).Skip(start - 1), limit);
        if (window.Count == 0) return $"offset {offset} is past the end of '{filePath}'.";

        return Render(window, start, hasMore, total?.ToString() ?? $"a {GameUtils.FormatBytes(size)} file",
            EncodingNote(probe));
    }

    private static IEnumerable<string> ReadLines(string full, TextEncodingProbe probe)
    {
        using FileStream stream = File.OpenRead(full);
        stream.Position = probe.BomLength;
        using StreamReader reader = new(stream, probe.Encoding ?? TextEncodingDetector.Utf8NoBom,
            detectEncodingFromByteOrderMarks: false);
        while (reader.ReadLine() is { } line) yield return line;
    }

    // 切行口径同 StreamReader.ReadLine（\n、\r\n 与单独的 \r），整读与流式读的行号才对得上
    private static List<Range> SplitLines(string text)
    {
        List<Range> lines = new();
        int start = 0;
        while (start < text.Length)
        {
            int end = text.AsSpan(start).IndexOfAny('\r', '\n');
            if (end < 0)
            {
                lines.Add(start..text.Length);
                break;
            }

            end += start;
            lines.Add(start..end);
            start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
        }

        return lines;
    }

    // 负数从末尾倒数：-100 是最后 100 行。0 与 1 一样从头读
    private static int StartLine(int offset, int total) =>
        offset < 0 ? Math.Max(1, total + offset + 1) : Math.Max(1, offset);

    private static (List<string> Window, bool HasMore) TakeWindow(IEnumerable<string> lines, int? limit)
    {
        // limit=-1：全文读模式，绕过字节上限。其余不传 limit 时套默认窗口，总量另设保险——
        // 编码会话一次误读大文件就是几万 token，截断必须由工具侧兜底
        bool fullFile = limit == -1;
        int maxLines = fullFile ? int.MaxValue : Math.Max(1, limit ?? DefaultLineLimit);
        List<string> window = new();
        int totalBytes = 0;
        foreach (string line in lines)
        {
            if (!fullFile && (window.Count >= maxLines || totalBytes >= MaxTotalBytes)) return (window, true);

            string shown = line.Length > MaxLineChars ? ToolOutputTruncation.TruncateLine(line, MaxLineChars) : line;
            totalBytes += Encoding.UTF8.GetByteCount(shown) + 1;
            window.Add(shown);
        }

        return (window, false);
    }

    private static string Render(List<string> window, int start, bool hasMore, string total, string? encodingNote)
    {
        StringBuilder sb = new(string.Join('\n', window));
        if (hasMore)
        {
            // 带上总量：光说"续读 offset=31"，模型不知道是再翻一页就完还是要翻六十页，
            // 也就无从判断该继续翻还是改用 Grep 定位
            int next = start + window.Count;
            sb.Append('\n').Append(ReadTruncationNote.Format(start, next - 1, total, next));
        }

        if (encodingNote != null) sb.Append('\n').Append(encodingNote);
        return sb.ToString();
    }

    // 只在不是 UTF-8 时说：模型若改用 Shell 或脚本动这个文件，得知道它是什么编码
    private static string? EncodingNote(TextEncodingProbe probe) => probe.Verdict switch
    {
        ETextEncodingVerdict.Unknown => "[encoding not recognized; non-ASCII text may be garbled]",
        _ when probe.IsUtf8 => null,
        _ => $"[encoding: {probe.Encoding!.WebName}]",
    };

    private static string BinaryNotice(string filePath, long size) =>
        $"'{filePath}' is a binary file ({GameUtils.FormatBytes(size)}); its content can't be shown as text.";

    private async Task<string> NotFoundAsync(string filePath, string full, CancellationToken ct)
    {
        string missing = $"File '{filePath}' not found.";
        string name = Path.GetFileName(full);
        if (name.IndexOfAny(['*', '?']) >= 0)
        {
            return $"{missing} filePath takes one exact path; use `{FileToolNames.Glob}` to match a pattern.";
        }

        // 只在工作区里找；名字里带 glob 元字符的不找，拼进模式会被当成通配
        bool searchable = name.Length > 0 && name.IndexOfAny(['[', ']', '{', '}']) < 0
                          && _paths.WorkspaceRoot.Length > 0
                          && SearchRoot.NearestExistingAncestorWithin(_paths.WorkspaceRoot, full) != null;
        if (!searchable) return missing;

        IReadOnlyList<string> sameName = await FindSameNameAsync(name, ct).ConfigureAwait(false);
        return sameName.Count == 0
            ? missing
            : $"{missing} Files with the same name in the working directory:\n"
              + string.Join('\n', sameName.Select(path => "- " + path));
    }

    private async Task<IReadOnlyList<string>> FindSameNameAsync(string name, CancellationToken ct)
    {
        using CancellationTokenSource budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(SameNameSearchBudget);
        try
        {
            GlobOutcome outcome = await _glob.SearchAsync("**/" + name, maxResults: MaxSameNameFiles, ct: budget.Token)
                .ConfigureAwait(false);
            return outcome.Entries.Where(entry => !entry.IsDirectory).Select(entry => entry.Path).ToList();
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return [];
        }
    }
}
