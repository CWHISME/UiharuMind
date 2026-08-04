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
        _rootDirectory = rootDirectory;
    }

    /// <summary>
    /// 允许传绝对路径（外部目录），或相对路径（基于 workspaceRoot）。
    /// </summary>
    public async Task<List<string>> SearchAsync(
        string pattern,
        string? root = null,
        int maxResults = 300,
        CancellationToken ct = default)
    {
        // 解析搜索根
        string searchRoot = string.IsNullOrWhiteSpace(root)
            ? _rootDirectory
            : Path.IsPathFullyQualified(root)
                ? root
                : Path.GetFullPath(Path.Combine(_rootDirectory, root));

        if (!Directory.Exists(searchRoot)) return new List<string> { $"[Error] Directory not found: '{root}'" };

        // 无通配符退化：LLM 经常把绝对路径当 pattern 传
        if (!LooksLikeGlob(pattern))
        {
            string candidate = ResolveCandidate(pattern, searchRoot);
            if (File.Exists(candidate))
            {
                // 直接透传返回，不进 Glob 引擎
                return new List<string> 
                { 
                    $"[FILE] {Path.GetRelativePath(searchRoot, candidate).Replace('\\', '/')}" 
                };
            }

            // 文件不存在，友好提示切 Tool
            return new List<string>
            {
                $"[Error] glob pattern '{pattern}' has no wildcards."
            };
        }

        // 正常走 Glob
        Glob glob;
        try { glob = Glob.Parse(pattern.TrimEnd('/'), GlobOptions.IgnoreCase); }
        catch { return new List<string> { $"[Error] Invalid glob pattern: '{pattern}'" }; }

        bool dirsOnly = pattern.EndsWith('/');

        using var enumerator = new GlobEnum(glob, HardSkips, searchRoot, dirsOnly, maxResults);
        var list = new List<string>(Math.Min(maxResults, 60));

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
        
        list.Sort();
        if (hitLimit) list.Add($"... truncated (>{maxResults})");
        return list;
    }
    
    private static bool LooksLikeGlob(string s)
        => s.IndexOfAny(['*', '?', '{', '[']) >= 0 || s.Contains("**");

    private static string ResolveCandidate(string pattern, string root)
        => Path.IsPathFullyQualified(pattern)
            ? Path.GetFullPath(pattern)
            : Path.GetFullPath(Path.Combine(root, pattern));

    // ── 核心：零分配剪枝枚举 ──
    private sealed class GlobEnum : FileSystemEnumerator<string>
    {
        private readonly Glob _glob;
        private readonly GlobCollection _skip;
        private readonly string _root;
        private readonly bool _dirsOnly;
        public bool HitLimit { get; private set; }
        private int _count, _cap;

        public GlobEnum(Glob glob, GlobCollection skip, string root, bool dirsOnly, int cap)
            : base(root, new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.Hidden | FileAttributes.ReparsePoint | FileAttributes.System
            })
        {
            _glob = glob; _skip = skip; _root = root;
            _dirsOnly = dirsOnly; _cap = cap;
        }

        protected override string TransformEntry(ref FileSystemEntry e)
        {
            if (++_count > _cap) { HitLimit = true; }

            string full = Path.Join(e.Directory, e.FileName);
            string rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            return e.Attributes.HasFlag(FileAttributes.Directory)
                ? $"[DIR]  {rel}"
                : $"[FILE] {rel}";
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