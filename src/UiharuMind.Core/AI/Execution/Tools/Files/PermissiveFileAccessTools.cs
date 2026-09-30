/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Harness;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution.Files;

// [MFA绕坑] 绕:框架 FileAccessProvider 拒绝一切绝对路径 因:该类 internal 无法继承修改 删除条件:框架允许配置路径策略
/// <summary>
/// 自带的文件工具集,替代 MFA 内置的 FileAccessProvider。
/// MFA 的 FileAccessProvider 在调用存储前会用 StorePaths.NormalizeRelativePath 拒绝一切绝对路径,
/// 且该类为 internal 无法继承/修改;因此这里完全自行实现文件访问:
/// - 相对路径解析到工作区根目录;
/// - 绝对路径直接访问真实文件系统。
///
/// <b>工作区外的写入没有在这一层拦</b>,拦在审批规则里(<c>ApprovalModeMapper</c>):
/// 任何权限档下首次越界写入都要用户点一次,包括完全自动档。放在那一层是因为「越界」是<b>授权</b>
/// 问题而不是路径解析问题——同一条判据还要服务 shell,而且用户点了"本会话允许"之后要能真的放行。
///
/// 五个工具:Read / Write / Edit / Glob / Grep。
/// Glob 采用 Meziantou.Framework.Globbing 实现递归路径枚举;Grep 采用 Glacier.Grep 高性能检索引擎
/// (它自带 .gitignore/.ignore/.rgignore 的层级排除,对齐 ripgrep 行为)。
/// 编辑语义(唯一匹配/重叠检测/保守 fuzzy/落盘保真)全在 <see cref="FileEditPlanner"/>,
/// Grep 结果整形在 <see cref="GrepResultShaper"/>;本类只负责路径解析、限幅与落盘;Write 覆盖前经 <see cref="IFileBackupStore"/> 自动备份。
/// 写工具各包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
/// </summary>
internal sealed class PermissiveFileAccessTools
{
    // —— 输出限幅:工具输出直接进模型上下文,编码会话的上下文大头是工具结果而非对话。
    //    Grep 的限幅在 GrepResultShaper;Glob 在 SimpleGlobber 内限 300 条;shell 由框架截断。——
    internal const int DefaultReadLineLimit = 2000; //未显式传 limit 时的行数上限

    /// <summary>
    /// Read 单次返回的总量上限，按 <b>UTF-8 字节</b>算而不是字符：中文约 1~1.5 字符/token，
    /// 按字符算实际能放进标称值三四倍的 token。与 Grep 的正文预算同口径；要全文走 <c>limit=-1</c>。
    /// </summary>
    internal const int MaxReadTotalBytes = 32 * 1024;

    internal const int MaxReadLineChars = 2000; //单行截断(压缩产物一行可达数百 KB)

    /// <summary>Edit 回给模型的 diff 行数上限（与审批卡片共用，见 <c>FileEditPlanner.DefaultMaxDiffLines</c>）</summary>
    internal const int MaxEditDiffLines = FileEditPlanner.DefaultMaxDiffLines;

    /// <summary>Edit diff 单行长度上限（minified JSON 一行可达几十 KB，行数上限拦不住）</summary>
    internal const int MaxEditDiffLineChars = FileEditPlanner.DefaultMaxDiffLineChars;

    private const string SearchPathDescription = //Glob 与 Grep 的 path 参数共用
        "Omit to search the whole working directory. "
        + "Pass a subdirectory or a single file only to narrow the search; relative or absolute. "
        + "Returned paths are relative to the working directory.";

    /// <summary>
    /// 同文件的"读→计划→写"互斥锁。<c>AllowConcurrentInvocation=true</c> 时同一轮的两个 Edit/Write
    /// 打同一文件会静默 lost-update；锁表进程级静态，主代理与子代理的实例天然互斥。
    ///
    /// 用定长 striped 数组而不是字典：内存有界，碰撞的代价只是偶发伪串行。key 大小写不敏感
    /// （macOS/Windows 默认文件系统如此）。父目录是符号链接的别名路径会拿到不同条纹，
    /// 与 ApprovalModeMapper"不解析符号链接"的口径一致，接受（见 <see cref="ResolveWriteTarget"/>）。
    /// </summary>
    private const int FileLockStripes = 64;
    private static readonly SemaphoreSlim[] _fileLocks = BuildFileLocks();

    private static SemaphoreSlim[] BuildFileLocks()
    {
        SemaphoreSlim[] locks = new SemaphoreSlim[FileLockStripes];
        for (int i = 0; i < locks.Length; i++) locks[i] = new SemaphoreSlim(1, 1);
        return locks;
    }

    private static SemaphoreSlim LockFor(string path)
        => _fileLocks[(uint)StringComparer.OrdinalIgnoreCase.GetHashCode(path) % _fileLocks.Length];

    private readonly string _workspaceRoot;
    private readonly SimpleGlobber _glob;
    private readonly SimpleGrepper _grepper;
    private readonly IFileBackupStore _backupStore; //组合持有:Write 覆盖前备份用,可注入

    public PermissiveFileAccessTools(string workspaceRoot, IFileBackupStore? backupStore = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _glob = new SimpleGlobber(workspaceRoot);
        _grepper = new SimpleGrepper(workspaceRoot);
        _backupStore = backupStore ?? new FileBackupStore();
        Directory.CreateDirectory(_workspaceRoot);
    }

    public IReadOnlyList<AITool> Create(bool disableWriteTools = false)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(Read, ToolOptions(FileToolNames.Read)),
            AIFunctionFactory.Create(Glob, ToolOptions(FileToolNames.Glob)),
            AIFunctionFactory.Create(Grep, ToolOptions(FileToolNames.Grep)),
        };

        if (!disableWriteTools)
        {
            tools.Add(Wrap(AIFunctionFactory.Create(Write, ToolOptions(FileToolNames.Write))));
            tools.Add(Wrap(AIFunctionFactory.Create(Edit, ToolOptions(FileToolNames.Edit))));
        }

        return tools;

        static AITool Wrap(AIFunction function) => new ApprovalRequiredAIFunction(function);

        // 宽容口径:fileGlobs 是 string[],而模型常给一个标量字符串,
        // 从前那会死在反序列化上并回一句它看不懂的框架异常(见 ToolJson)
        static AIFunctionFactoryOptions ToolOptions(string name) => ToolJson.CreateFactoryOptions(name);
    }

    [Description("Find files by glob pattern.")]
    private async Task<GlobToolResult> Glob(
        [Description("Glob pattern, e.g. \"**/*.cs\".")] string pattern,
        [Description(SearchPathDescription)] string? path = null)
    {
        GlobOutcome outcome = await _glob.SearchAsync(pattern, path).ConfigureAwait(false);

        if (outcome.Failure != null)
        {
            return new GlobToolResult
            {
                Notice = SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep),
            };
        }

        // 每个文件都标大小:纪律段要它别把大文件整个拉进来,这是判断依据(一条约 4 token,
        // 误读一次大文件是上万 token)。只标超阈值的不行——没标注就分不清"小"和"没测"。
        // 文件在前、目录在后:文件才是下一步 Read/Edit 的对象;按纯字典序两者会逐行交替,很难扫。
        // 排序只在这一层做,界面那侧有自己的习惯(见 SearchService)
        return new GlobToolResult
        {
            Entries = outcome.Entries
                .OrderBy(x => x.IsDirectory)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .Select(Render)
                .ToList(),
            Notice = BuildGlobNotice(outcome),
        };

        static string Render(GlobEntry entry) => entry.IsDirectory
            ? $"[DIR]  {entry.Path}"
            : $"[FILE] {entry.Path} ({GameUtils.FormatBytes(entry.SizeBytes)})";
    }

    private static string? BuildGlobNotice(GlobOutcome outcome)
    {
        if (outcome.Entries.Count == 0)
        {
            return $"Searched \"{outcome.ResolvedDirectory}\" — 0 matches; the path exists, "
                   + "nothing matched the pattern.";
        }

        return outcome.Truncated
            ? $"Showing the first {outcome.Entries.Count} entries; more were dropped. "
              + "Narrow the pattern or scope it with path."
            : null;
    }

    // 命中过多时怎么办只由地图的 Notice 说:它就贴在触发那一刻,描述里再写一遍是每轮白付
    [Description("Search file contents. Respects .gitignore.")]
    internal async Task<GrepToolResult> Grep(
        [Description("Search pattern (ripgrep syntax).")] string pattern,
        [Description("Treat the pattern as a regular expression. "
                     + "A pattern that does not compile as one is retried as a literal string, "
                     + "and the result says so.")]
        bool isRegex = true,
        [Description("Case-sensitive search.")] bool caseSensitive = false,
        [Description("How many lines of context to show around each match.")] int contextLines = 0,
        [Description("Maximum directory depth to walk (null means no limit).")] int? maxDepth = null,
        // 带路径或 **/ 前缀的 glob 在搜索器里已剥成裸文件名,不必再警告模型别这么写
        [Description("File-name globs, e.g. [\"*.cs\"]. Matched against the file name only; "
                     + "scope by folder with path.")]
        string[]? fileGlobs = null,
        [Description(SearchPathDescription)] string? path = null,
        CancellationToken ct = default)
    {
        GrepOutcome outcome = await _grepper
            .SearchAsync(pattern, isRegex, caseSensitive, contextLines, maxDepth, fileGlobs, path, ct)
            .ConfigureAwait(false);
        return GrepResultShaper.Shape(outcome, pattern);
    }

    // 描述只写契约;何时读全文、何时先 Grep 再切片是策略,归文件纪律段(AgentToolPrompts.FileReadDefault)
    [Description("Read a file's raw content. By default returns at most 2000 lines or 32KB per call, "
                 + "whichever comes first; a trailing notice gives the offset to continue from.")]
    internal Task<string> Read(
        [Description("File path, absolute or relative to the working directory.")] string filePath,
        [Description("1-based starting line.")] int offset = 1,
        [Description("Max lines to return. Pass -1 to read the whole file (no byte cap).")] int? limit = null,
        CancellationToken cancellationToken = default)
    {
        string full = ResolvePath(filePath);
        if (!File.Exists(full)) return Task.FromResult($"File '{filePath}' not found.");

        if (offset < 1) offset = 1;
        // limit=-1:全文读模式,绕过字节上限,读完整个文件
        // 其余:不传 limit 时套默认窗口,总量另设保险——
        // 编码会话一次误读大文件就是几万 token,截断必须由工具侧兜底
        bool fullFile = limit == -1;
        int effectiveLimit = fullFile ? int.MaxValue : Math.Max(1, limit ?? DefaultReadLineLimit);

        var lines = new List<string>();
        bool hasMore = false;
        int totalBytes = 0;
        using var reader = new StreamReader(full, Encoding.UTF8);
        for (int current = 1; current < offset; current++)
        {
            if (reader.ReadLine() is null) break;
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (!fullFile && (lines.Count >= effectiveLimit || totalBytes >= MaxReadTotalBytes))
            {
                hasMore = true;
                break;
            }

            if (line.Length > MaxReadLineChars) line = ToolOutputTruncation.TruncateLine(line, MaxReadLineChars);
            totalBytes += Encoding.UTF8.GetByteCount(line) + 1;
            lines.Add(line);
        }

        if (lines.Count == 0) return Task.FromResult($"File '{filePath}' is empty or offset is beyond its end.");

        string content = string.Join('\n', lines);
        if (!hasMore) return Task.FromResult(content);

        int nextOffset = offset + lines.Count;
        // 带上文件总大小:光说"续读 offset=31",模型不知道是再翻一页就完还是要翻六十页,
        // 也就无从判断该继续翻还是改用 `Grep` 定位。总行数会更贴(offset/limit 按行算),
        // 但那要读到文件末尾才知道——正好违背这个工具存在的意义;文件大小是 O(1) 的代理值
        string size = GameUtils.FormatBytes(new FileInfo(full).Length);
        return Task.FromResult(
            $"{content}\n…[truncated: showing lines {offset}–{nextOffset - 1} of a {size} file; "
            + $"continue with offset={nextOffset}]");
    }

    [Description("Create a new file, or replace an existing one wholesale. Use 'Edit' for partial changes.")]
    internal async Task<string> Write(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("Full file content.")] string content,
        CancellationToken ct = default)
    {
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(ResolvePath(filePath));
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[]? originalBytes = null; //已有文件才有,顺手读来做备份与信封
            if (File.Exists(full)) originalBytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);

            // 覆盖已有文件时沿用它的 BOM 与行尾风格;新建文件则是无 BOM + 模型给的 \n。
            // 不这么做的话,让模型重写一个 CRLF 文件会顺手把整份文件的行尾改掉
            TextFileEnvelope envelope = originalBytes is null
                ? TextFileEnvelope.FromText(string.Empty)
                : TextFileEnvelope.FromBytes(originalBytes);

            string? backupPath = null; //新建文件无备份
            if (originalBytes is not null)
                backupPath = await _backupStore.BackupAsync(full, originalBytes, ct).ConfigureAwait(false);

            await SaveAsync(full, envelope, envelope.ConvertNewLines(content), ct).ConfigureAwait(false);
            int lines = content.Split('\n').Length;
            return backupPath is null
                ? $"{FileToolNames.WriteSucceededPrefix}{filePath}' ({lines} lines)."
                : $"{FileToolNames.WriteSucceededPrefix}{filePath}' ({lines} lines). Previous version backed up to '{backupPath}'.";
        }
        finally
        {
            fileLock.Release();
        }
    }

    [Description("""
                 Change an existing file by exact text replacement.
                 - Put every change to one file in a single call, as multiple entries in `edits`.
                 - Every oldString is matched against the file as it is now, not against
                   your earlier entries in the same call.
                 - Entries must not overlap: merge nearby changes into one entry, keep distant ones separate.
                 - Nothing is written unless every entry applies; the error tells you what to fix.
                 """)]
    internal async Task<string> Edit(
        [Description("File path, absolute or relative to the working directory.")]
        string filePath,
        [Description("The replacements to apply.")] List<FileEdit> edits,
        CancellationToken ct = default)
    {
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(ResolvePath(filePath));
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            FileEditPlan plan = await FileEditPlanner.PlanFileAsync(full, filePath, edits, ct).ConfigureAwait(false);
            if (!plan.Succeeded) return $"[Edit failed] {plan.Error}";

            await SaveAsync(full, plan.Envelope, plan.NewText, ct).ConfigureAwait(false);

            string diff = FileEditPlanner.RenderDiff(plan.Diff, MaxEditDiffLines, MaxEditDiffLineChars);
            return $"{FileToolNames.EditSucceededPrefix}{edits.Count} edit(s) to '{filePath}'.\n{diff}";
        }
        finally
        {
            fileLock.Release();
        }
    }

    // 统一落盘:BOM 与行尾按信封原样还原。先写同目录临时文件再 rename 替换(跨文件系统会失去原子性),
    // 读方永远看到完整的旧或新内容,所以 Read/Grep 不进锁;不 fsync,不保证断电后在盘。
    // 固有代价:rename 会断开硬链接(其它链接仍指向旧 inode),vim 式原子写的通病
    private static async Task SaveAsync(string full, TextFileEnvelope envelope, string content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        string temp = Path.Combine(Path.GetDirectoryName(full)!,
            $".{Path.GetFileName(full)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");
        try
        {
            await File.WriteAllBytesAsync(temp, envelope.ToBytes(content), ct).ConfigureAwait(false);

            // 原子替换会新建 inode,原文件的权限/可执行位必须搬到 temp 再换,
            // 否则编辑一个 755 的脚本后它变成 644(执行位静默丢失)。非 Unix 平台或
            // 权限复制失败都不阻断写入。
            if (File.Exists(full))
            {
                try
                {
                    if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
                    {
                        File.SetUnixFileMode(temp, File.GetUnixFileMode(full));
                    }
                }
                catch
                {
                    // 权限读取失败:保留默认权限继续写
                }
            }

            try
            {
                File.Move(temp, full, overwrite: true);
            }
            catch (IOException) when (File.Exists(full))
            {
                // Windows:目标被其它进程打开(未开 FILE_SHARE_DELETE)时 Move 抛 IOException。
                // 回退直接写目标——失去原子性但保住可用性,模型拿到的仍是成功结果而不是裸异常。
                await File.WriteAllBytesAsync(full, envelope.ToBytes(content), ct).ConfigureAwait(false);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch
            {
                // 临时文件清理失败不影响主路径(目标已由 Move 决定)
            }
        }
    }

    // ---- 路径解析 ----

    private string ResolvePath(string path)
    {
        return Path.IsPathRooted(path)
            ? Path.GetFullPath(path)
            : Path.GetFullPath(Path.Combine(_workspaceRoot, path));
    }

    /// <summary>
    /// 写目标跟随符号链接到真实路径，锁与原子写都对着它做：否则 rename 会把链接<b>本体</b>静默换成
    /// 普通文件，且链接与目标在锁表里拿不同条纹。解析失败（例如文件不存在）回退原路径。
    ///
    /// 已知边界：只解析末段——父目录是链接时别名路径仍拿不同条纹，并发别名编辑可能 lost-update。
    /// 与 <c>ApprovalModeMapper</c>"不解析符号链接"口径一致；全路径 realpath 带来 TOCTOU，不划算。
    /// </summary>
    private static string ResolveWriteTarget(string full)
    {
        try
        {
            FileSystemInfo? target = File.ResolveLinkTarget(full, returnFinalTarget: true);
            return target?.FullName ?? full;
        }
        catch
        {
            return full; // 解析失败（文件不存在等）回退原路径
        }
    }
}
