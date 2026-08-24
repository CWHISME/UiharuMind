using System.IO.Enumeration;

namespace UiharuMind.Core.AI.Execution.Files;

using System.IO;
using Meziantou.Framework.Globbing;

/// <summary>
/// 搜索文件：标准 glob 语法
/// </summary>
public sealed class SimpleGlobber
{
    private static readonly GlobCollection HardSkips = new(
        Glob.Parse("**/node_modules/**", GlobOptions.IgnoreCase),
        Glob.Parse("**/.git/**", GlobOptions.IgnoreCase),
        Glob.Parse("**/bin/**", GlobOptions.IgnoreCase),
        Glob.Parse("**/obj/**", GlobOptions.IgnoreCase)
    );

    private string _rootDirectory;

    public SimpleGlobber(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
    }

    /// <summary>
    /// 按 glob 表达式搜文件。<b>失败与"搜到 0 条"分开返回</b>，见 <see cref="GlobOutcome"/>：
    /// 从前失败是塞一条 <c>"[Error] ..."</c> 进结果列表，界面的快速搜索会把它当成一个文件名显示。
    /// </summary>
    /// <param name="pattern">glob 表达式；目录可给绝对路径或相对工作区的相对路径</param>
    /// <param name="directory">搜索根，为空则用工作区根</param>
    /// <param name="maxResults">命中上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>命中条目与失败原因</returns>
    public async Task<GlobOutcome> SearchAsync(
        string pattern,
        string? directory = null,
        int maxResults = 300,
        CancellationToken ct = default)
    {
        string searchRoot = SearchRoot.Resolve(_rootDirectory, directory);

        if (!Directory.Exists(searchRoot))
        {
            return Failed(ESearchFailureKind.DirectoryNotFound, searchRoot, directory, pattern);
        }

        // 无通配符退化：LLM 经常把绝对路径当 pattern 传
        if (!LooksLikeGlob(pattern))
        {
            string candidate = ResolveCandidate(pattern, searchRoot);
            if (File.Exists(candidate))
            {
                // 直接透传返回，不进 Glob 引擎
                return new GlobOutcome
                {
                    ResolvedDirectory = searchRoot,
                    Entries = new List<GlobEntry>
                    {
                        new(SearchRoot.ToPortablePath(_rootDirectory, candidate), false,
                            new FileInfo(candidate).Length)
                    },
                };
            }

            return Failed(ESearchFailureKind.GlobHasNoWildcard, searchRoot, directory, pattern);
        }

        // 正常走 Glob
        Glob glob;
        try
        {
            glob = Glob.Parse(pattern.TrimEnd('/'), GlobOptions.IgnoreCase);
        }
        catch (Exception e)
        {
            return Failed(ESearchFailureKind.InvalidGlobPattern, searchRoot, directory, pattern, e.Message);
        }

        bool dirsOnly = pattern.EndsWith('/');

        using var enumerator = new GlobEnum(glob, HardSkips, searchRoot, _rootDirectory, dirsOnly, maxResults);
        var list = new List<GlobEntry>(Math.Min(maxResults, 60));

        bool hitLimit = false;
        while (enumerator.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            list.Add(enumerator.Current!);
            if (list.Count >= maxResults)
            {
                hitLimit = true;
                break;
            }
        }

        list.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return new GlobOutcome { Entries = list, ResolvedDirectory = searchRoot, Truncated = hitLimit };
    }

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
                WorkingDirectory = _rootDirectory,
                Pattern = pattern,
                Detail = detail,
            },
        };
    }

    private static bool LooksLikeGlob(string s)
        => s.IndexOfAny(['*', '?', '{', '[']) >= 0 || s.Contains("**");

    private static string ResolveCandidate(string pattern, string root)
        => Path.IsPathFullyQualified(pattern)
            ? Path.GetFullPath(pattern)
            : Path.GetFullPath(Path.Combine(root, pattern));

    // ── 核心：零分配剪枝枚举 ──
    private sealed class GlobEnum : FileSystemEnumerator<GlobEntry>
    {
        private readonly Glob _glob;
        private readonly GlobCollection _skip;
        private readonly string _root; //搜索根:glob 表达式是相对它匹配的
        private readonly string _workspaceRoot; //工作区根:输出路径相对它写,好让 Read 能直接吃
        private readonly bool _dirsOnly;
        public bool HitLimit { get; private set; }
        private int _count, _cap;

        public GlobEnum(Glob glob, GlobCollection skip, string root, string workspaceRoot, bool dirsOnly, int cap)
            : base(root, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.System
            })
        {
            _glob = glob; _skip = skip; _root = root; _workspaceRoot = workspaceRoot;
            _dirsOnly = dirsOnly; _cap = cap;
        }

        protected override GlobEntry TransformEntry(ref FileSystemEntry e)
        {
            if (++_count > _cap) { HitLimit = true; }

            string full = Path.Join(e.Directory, e.FileName);
            // 输出按工作区根:回给模型的路径要能直接当 Read 的入参(见 SearchRoot.ToPortablePath)。
            // 匹配用的 rel 仍按搜索根算,那是 glob 表达式的基准,两者不能混
            string rel = SearchRoot.ToPortablePath(_workspaceRoot, full);
            bool isDir = e.Attributes.HasFlag(FileAttributes.Directory);
            // e.Length 从枚举器已有的文件元数据里取,不额外走一次 stat
            return new GlobEntry(rel, isDir, isDir ? 0 : e.Length);
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry e)
        {
            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/');
            if (_skip.IsMatch(rel)) return false;

            bool isDir = e.Attributes.HasFlag(FileAttributes.Directory);
            return !(_dirsOnly && !isDir) && _glob.IsMatch(rel);
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry e)
        {
            // 剪枝：被硬排除 or 不可能命中用户 pattern，直接不进目录
            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/').TrimEnd('/');
            return !_skip.IsMatch(rel) && _glob.IsPartialMatch(rel.AsSpan());
        }
    }
}