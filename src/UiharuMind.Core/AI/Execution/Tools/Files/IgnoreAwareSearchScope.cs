/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.IO.Enumeration;
using Glacier.Grep;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 引擎的一次调用：从 <see cref="Root"/> 往下搜，<see cref="MaxDepth"/> 按引擎口径（递归进去的目录层数，0 = 只搜根下的文件）
/// </summary>
internal readonly record struct EngineRun(string Root, int? MaxDepth);

/// <summary>
/// 让 Grep 认得<b>搜索根之上</b>的忽略文件。
///
/// 引擎（Glacier.Grep 1.0.1）只从它的根往下加载 <c>.gitignore</c> 等，祖先目录里的规则一概看不见：
/// 仓库根写着 <c>/Code/build/</c>，搜 <c>path=Code</c> 就把 build 整个扫进来。实测 MyStory 搜 Code，
/// 1136 处命中里 1021 处落在被忽略的构建产物里；正则搜索还会把大文件整份解成一块 char[]，
/// 一次搜索就是 200MB+ 的大对象堆分配。
///
/// ⛔ 不能把引擎根上移到带忽略文件的祖先再过滤结果：窄路径会变成整库搜索，
/// 实测 SLG2（2.8 万文件）搜 <c>Logic/Kit</c> 从 3ms/1.4MB 变成 9 秒/1.3GB。
///
/// 做法分两半：
/// <list type="bullet">
/// <item><b>拆根管性能。</b>只走目录，找出被上层规则忽略、引擎却认不出的目录（洞），
/// 只沿通往洞的路径把搜索根拆成几次引擎调用；没有洞时就是原样一次调用。</item>
/// <item><b>过滤管正确。</b>每处命中按 git 口径再判一遍（任一级目录被忽略即忽略），
/// 拆根没顾到的（如引擎进了而这里没走的目录、被上层按文件名忽略的文件）都在这里兜住。</item>
/// </list>
/// 搜索根本身被忽略时（点名搜构建目录）不套上层规则，照搜。
/// </summary>
internal sealed class IgnoreAwareSearchScope
{
    private static readonly string[] IgnoreFileNames = [".gitignore", ".ignore", ".rgignore"]; //与引擎认的同一组
    private static readonly string[] EngineSkippedDirectories = ["bin", "obj", "node_modules", "release"]; //引擎写死不进的目录名

    private static readonly EnumerationOptions DirectoryOptions = new()
    {
        // 与引擎同一组：隐藏与系统属性的目录不进
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
    };

    private readonly string _workspace;
    private readonly string _root;
    private readonly bool _filters; //是否要按上层规则过滤命中
    private readonly Dictionary<string, List<GitIgnoreFile>> _ignoreFiles = new(StringComparer.OrdinalIgnoreCase); //目录 → 其下的忽略文件，按需加载
    private readonly Dictionary<string, bool> _directoryVerdicts = new(StringComparer.OrdinalIgnoreCase); //目录 → 是否被忽略
    private readonly Dictionary<string, bool> _fileVerdicts = new(StringComparer.OrdinalIgnoreCase); //命中文件 → 是否保留；同一文件常有多处命中
    private readonly Dictionary<string, List<string>> _children = new(StringComparer.OrdinalIgnoreCase); //走过的目录 → 未被忽略的子目录
    private readonly List<(string Path, string Decider)> _holes = []; //被忽略的目录，及作出判决的忽略文件所在目录
    private readonly List<EngineRun> _runs = [];

    private IgnoreAwareSearchScope(string workspace, string root, bool filters)
    {
        _workspace = workspace;
        _root = root;
        _filters = filters;
    }

    /// <summary>要依次交给引擎的调用</summary>
    public IReadOnlyList<EngineRun> Runs => _runs;

    /// <summary>
    /// 不套上层规则、原样一次调用（单文件搜索用）
    /// </summary>
    /// <param name="searchRoot">引擎根</param>
    /// <param name="maxDepth">深度限制</param>
    /// <returns>搜索范围</returns>
    public static IgnoreAwareSearchScope Whole(string searchRoot, int? maxDepth)
    {
        IgnoreAwareSearchScope scope = new(string.Empty, string.Empty, filters: false);
        scope._runs.Add(new EngineRun(searchRoot, maxDepth));
        return scope;
    }

    /// <summary>
    /// 算出一次目录搜索要怎么交给引擎
    /// </summary>
    /// <param name="workspaceRoot">工作区根，上层规则只认到它为止</param>
    /// <param name="searchRoot">调用方要搜的目录（绝对路径）</param>
    /// <param name="maxDepth">相对搜索根的深度限制</param>
    /// <returns>搜索范围</returns>
    public static IgnoreAwareSearchScope For(string workspaceRoot, string searchRoot, int? maxDepth)
    {
        string workspace = Normalize(workspaceRoot);
        string root = Normalize(searchRoot);
        if (!IsStrictlyUnder(root, workspace) || !HasIgnoreFileAbove(workspace, root)) return Whole(searchRoot, maxDepth);

        IgnoreAwareSearchScope scope = new(workspace, root, filters: true);
        if (scope.IsRootIgnored()) return Whole(searchRoot, maxDepth);

        scope.Walk(root, 0, maxDepth);
        scope.Plan(root, maxDepth);
        return scope;
    }

    /// <summary>
    /// 一处命中是否保留（按 git 口径：文件本身或其任一级目录被忽略即丢掉）
    /// </summary>
    /// <param name="absolutePath">命中文件的绝对路径</param>
    /// <returns>是否保留</returns>
    public bool Keeps(string absolutePath)
    {
        if (!_filters) return true;
        if (_fileVerdicts.TryGetValue(absolutePath, out bool cached)) return cached;

        bool keeps = !IsIgnoredFile(Normalize(absolutePath));
        _fileVerdicts[absolutePath] = keeps;
        return keeps;
    }

    private bool IsIgnoredFile(string file)
    {
        for (string dir = Parent(file); dir.Length > _root.Length; dir = Parent(dir))
        {
            if (IsIgnoredDirectory(dir)) return true;
        }

        return Decide(file, isDirectory: false).Ignored == true;
    }

    // 与 git 一致：工作区根到搜索根之间任一级目录被忽略，搜索根就算被忽略
    private bool IsRootIgnored()
    {
        for (string dir = _root; dir.Length > _workspace.Length; dir = Parent(dir))
        {
            if (IsIgnoredDirectory(dir)) return true;
        }

        return false;
    }

    private static bool HasIgnoreFileAbove(string workspace, string root)
    {
        for (string dir = Parent(root); dir.Length >= workspace.Length; dir = Parent(dir))
        {
            if (IgnoreFileNames.Any(name => File.Exists(Path.Combine(dir, name)))) return true;
        }

        return false;
    }

    // 只走引擎也会走的目录；洞记下来就不再往里走
    private void Walk(string dir, int depth, int? maxDepth)
    {
        if (maxDepth != null && depth + 1 > maxDepth) return;

        List<string> children = [];
        IEnumerable<DirectoryInfo> entries;
        try
        {
            entries = new DirectoryInfo(dir).EnumerateDirectories("*", DirectoryOptions);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return;
        }

        foreach (DirectoryInfo entry in entries)
        {
            if (IsEngineSkipped(entry.Name)) continue;

            string child = dir + "/" + entry.Name;
            (bool? ignored, string decider) = Decide(child, isDirectory: true);
            _directoryVerdicts[child] = ignored == true;
            if (ignored == true)
            {
                _holes.Add((child, decider));
                continue;
            }

            children.Add(child);
            Walk(child, depth + 1, maxDepth);
        }

        _children[dir] = children;
    }

    // 洞的判决来自引擎根之上的忽略文件时，引擎认不出它，只能把这个根拆开：根下的散文件单搜，子目录各自再判
    private void Plan(string dir, int? budget)
    {
        bool blind = _holes.Any(h => IsStrictlyUnder(h.Path, dir) && !IsUnderOrSelf(h.Decider, dir));
        if (!blind)
        {
            _runs.Add(new EngineRun(dir, budget));
            return;
        }

        _runs.Add(new EngineRun(dir, 0));
        if (budget is <= 0) return;

        foreach (string child in _children.GetValueOrDefault(dir) ?? [])
        {
            Plan(child, budget - 1);
        }
    }

    private bool IsIgnoredDirectory(string dir)
    {
        if (_directoryVerdicts.TryGetValue(dir, out bool cached)) return cached;

        bool ignored = Decide(dir, isDirectory: true).Ignored == true;
        _directoryVerdicts[dir] = ignored;
        return ignored;
    }

    // 与引擎同一口径：从最近一级目录往上找，第一个给出判决的忽略文件说了算。
    // 调用方保证各级祖先目录已判过且未被忽略，所以只拿规则匹配路径本身
    private (bool? Ignored, string Decider) Decide(string path, bool isDirectory)
    {
        for (string dir = Parent(path); dir.Length >= _workspace.Length; dir = Parent(dir))
        {
            foreach (GitIgnoreFile file in IgnoreFilesOf(dir))
            {
                bool? verdict = MatchSelf(file, path, isDirectory);
                if (verdict != null) return (verdict, dir);
            }

            if (dir.Length == _workspace.Length) break;
        }

        return (null, string.Empty);
    }

    // 库里的 GitIgnoreRule.Matches 对每条规则都把路径的各级前缀再匹配一遍（规则数 × 深度²），
    // SLG2 根上 107 条规则、命中都在八九层深处，逐条命中判下来比扫描本身还慢。这里只匹配路径本身，
    // 口径与库一致：不带斜杠的规则比末段名字，带斜杠的比相对路径，同一文件里最后命中的规则说了算
    private static bool? MatchSelf(GitIgnoreFile file, string path, bool isDirectory)
    {
        if (!IsStrictlyUnder(path, file.DirectoryPath)) return null;

        ReadOnlySpan<char> relative = path.AsSpan(file.DirectoryPath.Length + 1);
        ReadOnlySpan<char> name = relative[(relative.LastIndexOf('/') + 1)..];
        bool? verdict = null;
        foreach (GitIgnoreRule rule in file.Rules)
        {
            if (rule.IsDirectoryOnly && !isDirectory) continue;
            if (FileSystemName.MatchesSimpleExpression(rule.CleanPattern, rule.IsRooted ? relative : name))
            {
                verdict = !rule.IsNegation;
            }
        }

        return verdict;
    }

    private List<GitIgnoreFile> IgnoreFilesOf(string directory)
    {
        if (_ignoreFiles.TryGetValue(directory, out List<GitIgnoreFile>? cached)) return cached;

        List<GitIgnoreFile> files = [];
        foreach (string name in IgnoreFileNames)
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;

            try
            {
                files.Add(new GitIgnoreFile(directory, File.ReadAllLines(path)));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // 读不了就当没有，与引擎同口径
            }
        }

        _ignoreFiles[directory] = files;
        return files;
    }

    private static bool IsEngineSkipped(string name) =>
        name.StartsWith('.') ||
        EngineSkippedDirectories.Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase));

    // macOS 默认文件系统大小写不敏感，与引擎的忽略匹配同一口径
    private static bool IsStrictlyUnder(string path, string dir) =>
        path.Length > dir.Length + 1 && path[dir.Length] == '/' &&
        path.StartsWith(dir, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnderOrSelf(string path, string dir) =>
        string.Equals(path, dir, StringComparison.OrdinalIgnoreCase) || IsStrictlyUnder(path, dir);

    private static string Parent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash > 0 ? path[..slash] : string.Empty;
    }

    private static string Normalize(string path) => Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
}
