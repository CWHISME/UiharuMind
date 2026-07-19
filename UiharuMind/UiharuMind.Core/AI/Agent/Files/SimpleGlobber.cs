using System.IO.Enumeration;

namespace UiharuMind.Core.AI.Agent.Files;

using System.IO;
using Meziantou.Framework.Globbing;

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
        string target = string.IsNullOrWhiteSpace(root)
            ? _rootDirectory
            : Path.IsPathFullyQualified(root)
                ? root
                : Path.GetFullPath(root);

        if (!Directory.Exists(target))
            return new List<string> { $"[Error] Directory not found: '{root}'" };

        Glob glob;
        try { glob = Glob.Parse(pattern.TrimEnd('/'), GlobOptions.IgnoreCase); }
        catch { return new List<string> { $"[Error] Invalid pattern: '{pattern}'" }; }

        bool dirsOnly = pattern.EndsWith('/');

        using var enumerator = new GlobEnum(glob, HardSkips, target, dirsOnly, maxResults);
        var list = new List<string>(Math.Min(maxResults, 60));

        while (enumerator.MoveNext())
        {
            ct.ThrowIfCancellationRequested();
            list.Add(enumerator.Current!);
            if (list.Count >= maxResults) break;
        }

        if (enumerator.HitLimit)
            list.Add($"... truncated (>{maxResults})");

        list.Sort();
        return list;
    }

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
            string full = Path.Join(e.Directory, e.FileName);
            string rel = Path.GetRelativePath(_root, full).Replace('\\', '/');
            return e.Attributes.HasFlag(FileAttributes.Directory)
                ? $"[DIR]  {rel}"
                : $"[FILE] {rel}";
        }

        protected override bool ShouldIncludeEntry(ref FileSystemEntry e)
        {
            if (++_count > _cap) { HitLimit = true; return false; }

            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/');
            if (_skip.IsMatch(rel)) return false;

            bool isDir = e.Attributes.HasFlag(FileAttributes.Directory);
            return !(_dirsOnly && !isDir) && _glob.IsMatch(rel);
        }

        protected override bool ShouldRecurseIntoEntry(ref FileSystemEntry e)
        {
            // 剪枝：被硬排除 or 不可能命中用户 pattern，直接不进目录
            string rel = Path.GetRelativePath(_root, Path.Join(e.Directory, e.FileName)).Replace('\\', '/');
            return !_skip.IsMatch(rel) && _glob.IsPartialMatch(rel.AsSpan());
        }
    }
}