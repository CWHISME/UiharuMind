using System.IO.Enumeration;

namespace UiharuMind.Core.AI.Execution.Files;

using System.IO;
using Meziantou.Framework.Globbing;

/// <summary>
/// 搜索文件：标准 glob 语法
/// </summary>
public sealed class SimpleGlobber
{
    /// <summary>
    /// 任何搜索都不该进的目录。写成 <c>**/名字</c> 而不是 <c>**/名字/**</c>：
    /// 本库的 <c>**</c> 只匹配目录段，末尾的 <c>/**</c> 之后还要再有一个文件段才算命中，
    /// 于是 <c>**/node_modules/**</c> 对任何路径都返回 false——排除从来不生效。
    /// 匹配目录名本身，再由 <see cref="GlobEnum.ShouldRecurseIntoEntry"/> 拦住下探即可。
    /// </summary>
    private static readonly GlobCollection HardSkips = new(
        Glob.Parse("**/node_modules", GlobDialect.Standard, GlobOptions.IgnoreCase),
        Glob.Parse("**/.git", GlobDialect.Standard, GlobOptions.IgnoreCase),
        Glob.Parse("**/bin", GlobDialect.Standard, GlobOptions.IgnoreCase),
        Glob.Parse("**/obj", GlobDialect.Standard, GlobOptions.IgnoreCase)
    );

    private readonly AgentPathResolver _paths;

    public SimpleGlobber(string rootDirectory) : this(new AgentPathResolver(rootDirectory))
    {
    }

    /// <summary>
    /// 按给定的路径口径搜索（agent 的文件工具用：与 Read/Write 同一份解析）
    /// </summary>
    /// <param name="paths">路径解析口径；其工作区根即默认搜索根</param>
    public SimpleGlobber(AgentPathResolver paths)
    {
        _paths = paths;
    }

    /// <summary>
    /// 按 glob 表达式搜文件。<b>失败与"搜到 0 条"分开返回</b>，见 <see cref="GlobOutcome"/>：
    /// 从前失败是塞一条 <c>"[Error] ..."</c> 进结果列表，界面的快速搜索会把它当成一个文件名显示。
    /// </summary>
    /// <param name="pattern">glob 表达式</param>
    /// <param name="path">搜索范围：目录或单个文件；绝对路径直接用，相对路径拼工作区，为空则用工作区根</param>
    /// <param name="maxResults">命中上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命中条目与失败原因</returns>
    public async Task<GlobOutcome> SearchAsync(
        string pattern,
        string? path = null,
        int maxResults = 300,
        CancellationToken ct = default)
    {
        if (!SearchRoot.TryResolve(_paths, path, out string target, out string pathError))
        {
            return Failed(ESearchFailureKind.InvalidPath, string.Empty, path, pattern, pathError);
        }

        bool isFileScope = !Directory.Exists(target) && File.Exists(target);
        if (!Directory.Exists(target) && !isFileScope)
        {
            return Failed(ESearchFailureKind.PathNotFound, target, path, pattern);
        }

        // 单文件搜索：根落在父目录，命中的条目再按精确路径滤到那一个文件
        string searchRoot = isFileScope ? Path.GetDirectoryName(target)! : target;

        // 无通配符退化：LLM 经常把绝对路径当 pattern 传
        if (!LooksLikeGlob(pattern))
        {
            string candidate = ResolveCandidate(pattern, searchRoot);
            if (File.Exists(candidate)
                && (!isFileScope
                    || string.Equals(Path.GetFullPath(candidate), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase)))
            {
                // 直接透传返回，不进 Glob 引擎
                return new GlobOutcome
                {
                    ResolvedDirectory = target,
                    Entries = new List<GlobEntry>
                    {
                        new(_paths.ToPortable(candidate), false,
                            new FileInfo(candidate).Length)
                    },
                };
            }

            return Failed(ESearchFailureKind.GlobHasNoWildcard, searchRoot, path, pattern);
        }

        // 正常走 Glob
        Glob glob;
        try
        {
            glob = Glob.Parse(pattern.TrimEnd('/'), GlobDialect.Standard, GlobOptions.IgnoreCase);
        }
        catch (Exception e)
        {
            return Failed(ESearchFailureKind.InvalidGlobPattern, searchRoot, path, pattern, e.Message);
        }

        bool dirsOnly = pattern.EndsWith('/');

        // 遍历是同步的,放到线程池上:界面的快速搜索在 UI 线程上调用,大目录树会把界面整个卡住
        (List<GlobEntry> list, bool hitLimit) = await Task.Run(
            () => Enumerate(glob, searchRoot, dirsOnly, AllowedDotSegments(pattern), maxResults, ct), ct)
            .ConfigureAwait(false);

        if (isFileScope)
        {
            // 精确路径过滤：同名的兄弟/深层文件不是目标
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (!string.Equals(_paths.Resolve(list[i].Path),
                        Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
                {
                    list.RemoveAt(i);
                }
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new GlobOutcome { Entries = list, ResolvedDirectory = target, Truncated = hitLimit };
    }

    private (List<GlobEntry> List, bool HitLimit) Enumerate(Glob glob, string searchRoot, bool dirsOnly,
        string[] allowedDotNames, int maxResults, CancellationToken ct)
    {
        using var enumerator = new GlobEnum(glob, HardSkips, searchRoot, _paths, dirsOnly, allowedDotNames, ct);
        var list = new List<GlobEntry>(Math.Min(maxResults, 60));
        while (enumerator.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            list.Add(enumerator.Current!);
            if (list.Count >= maxResults) return (list, true);
        }

        return (list, false);
    }

    // pattern 里点开头的段(如 .uiharu、.git*):只有点名了的点目录/点文件才放行,其余照旧跳过
    private static string[] AllowedDotSegments(string pattern)
        => pattern.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Where(segment => segment.StartsWith('.') && segment != "." && segment != "..")
            .ToArray();

    private GlobOutcome Failed(ESearchFailureKind kind, string resolved, string? requested,
        string pattern, string detail = "")
    {
        return new GlobOutcome
        {
            ResolvedDirectory = resolved,
            Failure = new SearchFailure
            {
                Kind = kind,
                RequestedDirectory = requested,
                ResolvedDirectory = resolved,
                WorkingDirectory = _paths.WorkspaceRoot,
                Pattern = pattern,
                Detail = detail,
                // PathNotFound 顺带回最近存活祖先，其余失败种类没有这个事实
                NearestExistingDirectory = kind == ESearchFailureKind.PathNotFound
                    ? SearchRoot.NearestExistingAncestorWithin(_paths.WorkspaceRoot, resolved) ?? string.Empty
                    : string.Empty,
            },
        };
    }

    // 模式退化成路径时相对的是搜索根;解析不了(如不认识的变量)就当没有这个文件,走"没有通配符"那条说明
    private string ResolveCandidate(string pattern, string searchRoot)
    {
        try
        {
            return _paths.Resolve(pattern, searchRoot);
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeGlob(string s)
        => s.IndexOfAny(['*', '?', '{', '[']) >= 0 || s.Contains("**");

    // ── 核心：零分配剪枝枚举 ──
    private sealed class GlobEnum : FileSystemEnumerator<GlobEntry>
    {
        private readonly Glob _glob;
        private readonly GlobCollection _skip;
        private readonly string _root; //搜索根:glob 表达式是相对它匹配的
        private readonly AgentPathResolver _paths; //输出路径按它写回,好让 Read 能直接吃
        private readonly bool _dirsOnly;
        private readonly string[] _allowedDotNames; //pattern 点名的点开头段,见 IsUnlistedDotEntry
        private readonly CancellationToken _ct; //命中稀少时 MoveNext 会在内部走很久不返回，取消只能在回调里认

        public GlobEnum(Glob glob, GlobCollection skip, string root, AgentPathResolver paths, bool dirsOnly,
            string[] allowedDotNames, CancellationToken ct)
            : base(root, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                // Unix 上 .NET 把点开头的名字都报成 Hidden,按属性跳过会让 pattern 点名了的 .uiharu 也搜不到;
                // 那边改由 IsUnlistedDotEntry 按名字判。Windows 的 Hidden 是真属性,照旧
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                                   | (OperatingSystem.IsWindows() ? FileAttributes.Hidden : 0)
            })
        {
            _glob = glob; _skip = skip; _root = root; _paths = paths;
            _dirsOnly = dirsOnly; _allowedDotNames = allowedDotNames; _ct = ct;
        }

        protected override GlobEntry TransformEntry(ref FileSystemEntry e)
        {
            string full = Path.Join(e.Directory, e.FileName);
            // 输出按工作区根:回给模型的路径要能直接当 Read 的入参(见 AgentPathResolver.ToPortable)。
            // 匹配用的 rel 仍按搜索根算,那是 glob 表达式的基准,两者不能混
            string rel = _paths.ToPortable(full);
            bool isDir = e.Attributes.HasFlag(FileAttributes.Directory);
            // e.Length 从枚举器已有的文件元数据里取,不额外走一次 stat
            return new GlobEntry(rel, isDir, isDir ? 0 : e.Length);
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry e)
        {
            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/');
            if (IsHardSkipped(rel) || IsUnlistedDotEntry(ref e)) return false; //不下探已经挡住了里面的东西，这里挡的是这个目录自己

            bool isDir = e.Attributes.HasFlag(FileAttributes.Directory);
            return !(_dirsOnly && !isDir) && _glob.IsMatch(rel);
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry e)
        {
            _ct.ThrowIfCancellationRequested();
            if (IsUnlistedDotEntry(ref e)) return false;
            // 剪枝：被硬排除 or 不可能命中用户 pattern，直接不进目录
            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/').TrimEnd('/');
            return !IsHardSkipped(rel) && _glob.IsPartialMatch(rel.AsSpan());
        }

        private bool IsUnlistedDotEntry(ref FileSystemEntry e)
        {
            if (OperatingSystem.IsWindows() || !e.FileName.StartsWith('.')) return false;
            foreach (string allowed in _allowedDotNames)
            {
                if (FileSystemName.MatchesSimpleExpression(allowed, e.FileName)) return false;
            }

            return true;
        }

        /// <summary>
        /// 硬排除判定。<c>GlobCollection.IsMatch</c> 的入参是<b>目录段与名字段两截</b>，
        /// 整条路径当一个参数传进去只会落在"目录"上、名字为空，于是恒为 false。
        /// 类型传 null：这几条 pattern 没有结尾斜杠，本库把它们归为文件型，
        /// 指名 <c>PathItemType.Directory</c> 反而匹配不上。
        /// </summary>
        /// <param name="relativePath">相对搜索根的路径，/ 分隔</param>
        /// <returns>是否命中硬排除</returns>
        private bool IsHardSkipped(string relativePath)
        {
            int slash = relativePath.LastIndexOf('/');
            ReadOnlySpan<char> directory = slash < 0 ? [] : relativePath.AsSpan(0, slash);
            return _skip.IsMatch(directory, relativePath.AsSpan(slash + 1), null);
        }
    }
}