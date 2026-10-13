using Glacier.Grep;

namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// glob 枚举用的忽略栈：与 grep 侧（Glacier <c>DirectoryTraverser</c>）同口径，
/// 认 <c>.gitignore</c> / <c>.ignore</c> / <c>.rgignore</c> 的层级规则。
/// 判定按"离条目最近的文件先判、有结论即停"（git 口径，<c>!</c> 否定照旧生效）。
///
/// <c>HardSkips</c> 不动，仍是无条件的前置短路：无 ignore 文件的作用域、
/// Windows 下的点目录（那边的点名规则不生效）都靠它；点目录规则也不动，
/// <c>.claude</c> 这种压根没进任何 ignore 的目录全靠那条才进不去。
///
/// 与 grep 侧 <c>CreateRootState</c> 的已知差异：那边是"上界到搜索根之间任一层
/// 被忽略就整体让路"，这里只看搜索根自身。对 <c>build/ + !build/keep/ +
/// build/keep/*.tmp</c> 这类组合两者结论可能不同；这里取的是字面语义
/// （点名的目录自己放行，里面的条目仍按规则），改之前先补测试。
/// </summary>
internal sealed class GlobIgnoreStack
{
    private static readonly string[] IgnoreFileNames = [".gitignore", ".ignore", ".rgignore"];

    private readonly string _topmost; //规则生效的上界：工作区根；单文件作用域退化成搜索根自己
    private readonly Dictionary<string, List<GitIgnoreFile>> _dirFiles = new(StringComparer.Ordinal);

    /// <summary>作用域里是否见过任何规则文件：没有时调用方跳过归一化与判定（零分配快路）</summary>
    public bool HasRules { get; private set; }

    /// <param name="searchRoot">枚举根（目录）。单文件作用域传其父目录，与上层调用保持一致</param>
    /// <param name="ignoreBoundary">规则上界（工作区根）；单文件作用域传 null，只认搜索根自己的文件</param>
    public GlobIgnoreStack(string searchRoot, string? ignoreBoundary)
    {
        string root = Normalize(searchRoot);
        _topmost = ignoreBoundary == null ? root : Normalize(ignoreBoundary);

        // 先把搜索根到上界整条链的文件都装进来，bypass 判定要看祖先的结论
        EnterDirectory(root);
        string? dir = ParentOf(root);
        while (dir != null && IsWithin(dir))
        {
            EnterDirectory(dir);
            if (dir.Equals(_topmost, StringComparison.OrdinalIgnoreCase)) break;
            dir = ParentOf(dir);
        }

        // 点名要搜的根自己被上层规则忽略时，上层规则整体让路（与 grep 侧 CreateRootState 同口径）：
        // 否则 path=Library 这种调用会把目标整个跳过
        if (ignoreBoundary != null && IsIgnoredByAncestors(root))
        {
            _dirFiles.Clear();
            HasRules = false;
            EnterDirectory(root);
        }
    }

    /// <summary>进入一个目录（下探前调）：它的 ignore 文件现在起对里面的条目生效</summary>
    /// <param name="normalizedDir">归一化全路径（/ 分隔、无尾斜杠）</param>
    public void EnterDirectory(string normalizedDir)
    {
        if (_dirFiles.ContainsKey(normalizedDir)) return;
        List<GitIgnoreFile> files = LoadIgnoreFiles(normalizedDir);
        _dirFiles[normalizedDir] = files;
        if (files.Count > 0) HasRules = true;
    }

    /// <summary>条目是否被忽略。目录也照判：被忽略的目录自己不收、下探另由调用方拦</summary>
    /// <param name="normalizedFullPath">归一化全路径</param>
    /// <param name="isDirectory">是否为目录</param>
    /// <returns>被忽略为 true</returns>
    public bool IsIgnored(string normalizedFullPath, bool isDirectory)
    {
        string? dir = ParentOf(normalizedFullPath);
        while (dir != null && IsWithin(dir))
        {
            if (_dirFiles.TryGetValue(dir, out List<GitIgnoreFile>? files))
            {
                foreach (GitIgnoreFile file in files)
                {
                    bool? verdict = file.IsIgnored(normalizedFullPath, isDirectory);
                    if (verdict != null) return verdict == true;
                }
            }

            if (dir.Equals(_topmost, StringComparison.OrdinalIgnoreCase)) break;
            dir = ParentOf(dir);
        }

        return false;
    }

    // 搜索根自己（不含它自己的文件）是否被上层规则忽略
    private bool IsIgnoredByAncestors(string normalizedRoot)
    {
        string? dir = ParentOf(normalizedRoot);
        while (dir != null && IsWithin(dir))
        {
            if (_dirFiles.TryGetValue(dir, out List<GitIgnoreFile>? files))
            {
                foreach (GitIgnoreFile file in files)
                {
                    bool? verdict = file.IsIgnored(normalizedRoot, true);
                    if (verdict != null) return verdict == true;
                }
            }

            if (dir.Equals(_topmost, StringComparison.OrdinalIgnoreCase)) break;
            dir = ParentOf(dir);
        }

        return false;
    }

    private bool IsWithin(string normalizedDir)
        => normalizedDir.Equals(_topmost, StringComparison.OrdinalIgnoreCase)
            || normalizedDir.StartsWith(_topmost + "/", StringComparison.OrdinalIgnoreCase);

    private static List<GitIgnoreFile> LoadIgnoreFiles(string normalizedDir)
    {
        List<GitIgnoreFile> files = new(IgnoreFileNames.Length);
        foreach (string name in IgnoreFileNames)
        {
            string path = normalizedDir + "/" + name;
            if (!File.Exists(path)) continue;
            try
            {
                files.Add(new GitIgnoreFile(normalizedDir, File.ReadAllLines(path)));
            }
            catch
            {
                // 读失败当不存在：搜索不能为一条规则文件整体失败
            }
        }

        return files;
    }

    /// <summary>归一化全路径：/ 分隔、无尾斜杠，供字典键与规则匹配共用</summary>
    public static string Normalize(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)).Replace('\\', '/');

    private static string? ParentOf(string normalizedPath)
    {
        int slash = normalizedPath.LastIndexOf('/');
        return slash < 0 ? null : normalizedPath.Substring(0, slash);
    }
}
