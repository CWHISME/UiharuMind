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
/// 解析口径只有一份(<see cref="AgentPathResolver"/>),搜索、审批、界面预演与这里同源。
///
/// <b>工作区外的写入没有在这一层拦</b>,拦在审批规则里(<c>ApprovalModeMapper</c>):
/// 任何权限档下首次越界写入都要用户点一次,包括完全自动档。放在那一层是因为「越界」是<b>授权</b>
/// 问题而不是路径解析问题——同一条判据还要服务 shell,而且用户点了"本会话允许"之后要能真的放行。
///
/// 五个工具:Read / Write / Edit / Glob / Grep。
/// Glob 采用 Meziantou.Framework.Globbing 实现递归路径枚举;Grep 采用 Glacier.Grep 高性能检索引擎
/// (它自带 .gitignore/.ignore/.rgignore 的层级排除,对齐 ripgrep 行为)。
/// 读(编码探测/分流看图/附注)全在 <see cref="FileReader"/>,编辑语义(唯一匹配/重叠检测/保守 fuzzy/落盘保真)
/// 全在 <see cref="FileEditPlanner"/>,Grep 结果整形在 <see cref="GrepResultShaper"/>;
/// 本类只负责工具面、写入与落盘;Write 覆盖前经 <see cref="IFileBackupStore"/> 自动备份。
/// 写工具各包一层 ApprovalRequiredAIFunction,沿用 MFA 的审批管线。
/// </summary>
internal sealed class PermissiveFileAccessTools
{
    // —— 输出限幅:工具输出直接进模型上下文,编码会话的上下文大头是工具结果而非对话。
    //    Read 的限幅在 FileReader;Grep 的在 GrepResultShaper;Glob 在 SimpleGlobber 内限 300 条;shell 由框架截断。——

    /// <summary>Edit 回给模型的 diff 行数上限（与审批卡片共用，见 <c>FileEditPlanner.DefaultMaxDiffLines</c>）</summary>
    internal const int MaxEditDiffLines = FileEditPlanner.DefaultMaxDiffLines;

    /// <summary>Edit diff 单行长度上限（minified JSON 一行可达几十 KB，行数上限拦不住）</summary>
    internal const int MaxEditDiffLineChars = FileEditPlanner.DefaultMaxDiffLineChars;

    // 描述只写契约;何时读全文、何时先 Grep 再切片是策略,归文件纪律段(AgentToolPrompts.FileReadDefault)
    private const string ReadDescription =
        "读取文件的原始内容。";

    private const string ReadImagesDescription = " PNG、JPEG 与 WebP 文件会以图片形式展示。"; //只有视觉模型的装配才说

    private const string SearchPathDescription = //Glob 与 Grep 的 path 参数共用
        "可选，省略则搜索整个工作目录。需要缩小搜索范围时传一个子目录或单个文件。";
    
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

    private readonly AgentPathResolver _paths;
    private readonly FileReader _reader;
    private readonly SimpleGlobber _glob;
    private readonly SimpleGrepper _grepper;
    private readonly IFileBackupStore _backupStore; //组合持有:Write 覆盖前备份用,可注入
    private readonly EReadImageMode _imageMode; //决定 Read 的描述提不提图片

    public PermissiveFileAccessTools(string workspaceRoot, IFileBackupStore? backupStore = null)
        : this(new AgentPathResolver(workspaceRoot), backupStore)
    {
    }

    /// <summary>
    /// 构造文件工具集
    /// </summary>
    /// <param name="paths">路径口径</param>
    /// <param name="backupStore">Write 覆盖前的备份；null 用默认目录</param>
    /// <param name="imageMode">Read 读到图片时怎么办，由装配按模型与识图开关定</param>
    public PermissiveFileAccessTools(AgentPathResolver paths, IFileBackupStore? backupStore = null,
        EReadImageMode imageMode = EReadImageMode.None)
    {
        _paths = paths;
        _reader = new FileReader(paths, imageMode, ImagePreview.Downscaler);
        _glob = new SimpleGlobber(paths);
        _grepper = new SimpleGrepper(paths);
        _backupStore = backupStore ?? new FileBackupStore();
        _imageMode = imageMode;
        Directory.CreateDirectory(paths.WorkspaceRoot);
    }

    public IReadOnlyList<AITool> Create(bool disableWriteTools = false)
    {
        var tools = new List<AITool>
        {
            AIFunctionFactory.Create(Read, ToolOptions(FileToolNames.Read,
                _imageMode == EReadImageMode.Attach ? ReadDescription + ReadImagesDescription : ReadDescription)),
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
        static AIFunctionFactoryOptions ToolOptions(string name, string? description = null) =>
            ToolJson.CreateFactoryOptions(name, description);
    }

    [Description("搜索文件")]
    internal async Task<string> Glob(
        [Description("glob 匹配模式，如 “**/*.c”")] string pattern,
        [Description("可选，工作目录的相对路径(也接受绝对路径)")] string? path = null,
        CancellationToken ct = default)
    {
        GlobOutcome outcome = await _glob.SearchAsync(pattern, path, ct: ct).ConfigureAwait(false);

        if (outcome.Failure != null)
        {
            return SearchFailureRenderer.Render(outcome.Failure, FileToolNames.Grep);
        }

        // 每个文件都标大小:纪律段要它别把大文件整个拉进来,这是判断依据(一条约 4 token,
        // 误读一次大文件是上万 token)。只标超阈值的不行——没标注就分不清"小"和"没测"。
        // 文件在前、目录在后:文件才是下一步 Read/Edit 的对象;按纯字典序两者会逐行交替,很难扫。
        // 排序只在这一层做,界面那侧有自己的习惯(见 SearchService)
        return ToolResultText.AppendNotice(
            string.Join('\n', outcome.Entries
                .OrderBy(x => x.IsDirectory)
                .ThenBy(x => x.Path, StringComparer.Ordinal)
                .Select(Render)),
            BuildGlobNotice(outcome));

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
    [Description("搜索文件内存在的指定内容")]
    internal async Task<string> Grep(
        [Description("匹配模式（ripgrep 语法）")] string pattern,
        [Description("可选，每处命中前后显示多少行上下文。")] int contextLines = 0,
        // 编译不过的正则会自动按字面串重搜并在 Notice 里说明,事前不必叮嘱
        [Description("可选，将匹配模式作为正则表达式处理")] bool isRegex = true,
        // [Description("区分大小写的搜索。")] bool caseSensitive = false,
        // 带路径或 **/ 前缀的 glob 在搜索器里已剥成裸文件名,不必再警告模型别这么写
        [Description("可选，按文件名的 glob 过滤，如 [\"*.cs\"]。只匹配文件名；按目录缩小范围用 path")]
        string[]? fileGlobs = null,
        [Description("可选，工作目录的相对路径(也接受绝对路径)，需要缩小搜索范围时传指定目录或单个文件路径")] 
        string? path = null,
        CancellationToken ct = default)
    {
        GrepOutcome outcome = await _grepper
            .SearchAsync(pattern, isRegex, false, contextLines, maxDepth: null, fileGlobs, path, ct)
            .ConfigureAwait(false);
        return GrepResultShaper.Shape(outcome, pattern);
    }

    internal Task<string> Read(
        [Description("文件路径，工作目录的相对路径(也接受绝对路径)。自动处理编码等问题、传 head=-1 可一次读完")] string filePath,
        [Description("起始行号，1 起算。负数从末尾计：-100 读最后 100 行")]
        int offset = 1,
        [Description("从 offset 开始，最多返回的行数")]
        int? head = null,
        CancellationToken cancellationToken = default)
        => _reader.ReadAsync(filePath, offset, head, cancellationToken);

    [Description("新建文件，或整份替换已有文件，局部修改用 Edit")]
    internal async Task<string> Write(
        [Description("文件路径，工作目录的相对路径(也接受绝对路径)")]
        string filePath,
        [Description("完整的文件内容")] string content,
        CancellationToken ct = default)
    {
        if (!_paths.TryResolve(filePath, out string resolved, out string pathError)) return $"[Write failed] {pathError}";
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(resolved);
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            byte[]? originalBytes = null; //已有文件才有,顺手读来做备份与信封
            if (File.Exists(full)) originalBytes = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);

            // 覆盖已有文件时沿用它的编码、BOM 与行尾风格;新建文件则是无 BOM 的 UTF-8 + 模型给的 \n。
            // 不这么做的话,让模型重写一个 CRLF 或 GBK 文件会顺手把整份文件的行尾或编码改掉
            TextFileEnvelope envelope = originalBytes is null
                ? TextFileEnvelope.FromText(string.Empty)
                : TextFileEnvelope.FromBytes(originalBytes);
            string body = envelope.ConvertNewLines(content);

            // 原编码写不下新内容(Big5、Shift-JIS、单字节编码才会)就改存 UTF-8 并说出来:
            // 整份替换的意图是明确的,不像 Edit 那样只改一小段却把整个文件换了编码
            string encodingNote = string.Empty;
            if (!envelope.CanEncode(body))
            {
                encodingNote = $" Saved as UTF-8: {envelope.EncodingName} can't store some of the new characters.";
                envelope = envelope.ToUtf8();
            }

            string? backupPath = null; //新建文件无备份
            if (originalBytes is not null)
                backupPath = await _backupStore.BackupAsync(full, originalBytes, ct).ConfigureAwait(false);

            await SaveAsync(full, envelope, body, ct).ConfigureAwait(false);
            int lines = content.Split('\n').Length;
            string backupNote = backupPath is null ? string.Empty : $" Previous version backed up to '{backupPath}'.";
            return $"{FileToolNames.WriteSucceededPrefix}{filePath}' ({lines} lines).{encodingNote}{backupNote}" +
                   MemoryListLine(full, body);
        }
        finally
        {
            fileLock.Release();
        }
    }

    [Description("""
                 按精确文本替换修改已有文件。
                 """)]
    internal async Task<string> Edit(
        [Description("文件路径，工作目录的相对路径(也接受绝对路径)。")]
        string filePath,
        [Description("要替换的条目。")] List<FileEdit> edits,
        CancellationToken ct = default)
    {
        if (!_paths.TryResolve(filePath, out string resolved, out string pathError)) return $"[Edit failed] {pathError}";
        // 写目标解析 symlink:锁与落盘都对着真实路径(否则链接被替换成普通文件)
        string full = ResolveWriteTarget(resolved);
        SemaphoreSlim fileLock = LockFor(full);
        await fileLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            FileEditPlan plan = await FileEditPlanner.PlanFileAsync(full, filePath, edits, ct).ConfigureAwait(false);
            if (!plan.Succeeded) return $"[Edit failed] {plan.Error}";

            await SaveAsync(full, plan.Envelope, plan.NewText, ct).ConfigureAwait(false);

            string diff = FileEditPlanner.RenderDiff(plan.Diff, MaxEditDiffLines, MaxEditDiffLineChars);
            return $"{FileToolNames.EditSucceededPrefix}{edits.Count} edit(s) to '{filePath}'." +
                   $"{MemoryListLine(full, plan.NewText)}\n{diff}";
        }
        finally
        {
            fileLock.Release();
        }
    }

    // 写的是记忆笔记时报出它在记忆索引里的样子(ADR 0076):首行空着、只是个标题、改了正文没改首行,当场看得见
    private string MemoryListLine(string full, string text)
    {
        if (!full.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || _paths.RelativeToMemory(full) is not { } name)
            return string.Empty;

        string summary = MemoryIndex.SummaryOf(text);
        return summary.Length > 0
            ? $"\nShown in the memory index as: {name} — {summary}"
            : $"\nShown in the memory index as: {name} (the first line is empty, so there is no summary)";
    }

    // 统一落盘:编码、BOM 与行尾按信封原样还原。先写同目录临时文件再 rename 替换(跨文件系统会失去原子性),
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
