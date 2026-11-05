namespace UiharuMind.Core.AI.Execution.Files;

/// <summary>
/// 一次列目录一层得到的一条子项。<b>枚举与判定只做一次</b>，两个消费方按自己的口径映射：
/// Read 工具（给模型看，路径转相对工作区）与快速搜索的浏览态（给界面看，路径转相对当前目录）。
/// </summary>
/// <param name="FullPath">绝对路径</param>
/// <param name="Name">名字（不含路径）</param>
/// <param name="IsDirectory">是目录还是文件</param>
/// <param name="SizeBytes">文件字节数；目录为 0</param>
/// <param name="IsHidden">点开头或是 Hidden 属性（两平台判法统一到这一处）</param>
/// <param name="IsSystem">带 System 属性（只 Windows 有意义；界面侧按它过滤）</param>
public readonly record struct DirectoryEntry(string FullPath, string Name, bool IsDirectory, long SizeBytes, bool IsHidden, bool IsSystem);

/// <summary>一次列目录的结果：条目与「是否达到上限、还有更多」</summary>
public readonly record struct DirectoryList(List<DirectoryEntry> Entries, bool HasMore);

/// <summary>
/// 「目录优先」收集的结果：额度先给目录、文件用剩下的，两类各自带「还有更多」。
/// 目录是模型下一步的落脚点（看不见子目录就无路可走），文件只是可选读物——
/// 枚举按名字走，目录名若排在文件之后，不预留额度就会被文件一路挤掉。
/// </summary>
/// <param name="Directories">目录，最多 maxItems 条</param>
/// <param name="HasMoreDirectories">目录超过额度，还有没列出的</param>
/// <param name="Files">文件，最多 maxItems - 目录数 条</param>
/// <param name="HasMoreFiles">文件超过额度，还有没列出的</param>
public readonly record struct DirectoryFirstListing(
    List<DirectoryEntry> Directories,
    bool HasMoreDirectories,
    List<DirectoryEntry> Files,
    bool HasMoreFiles);

/// <summary>
/// 列目录一层的枚举核心。Read 工具与快速搜索浏览态都要「把这个目录下的条目原样列出来」，
/// 从前各写一遍 <c>Directory.EnumerateFileSystemEntries</c> + 属性/大小/显隐判定，会漂移；
/// 这里收拢后两边只留各自的过滤与映射。
///
/// 语义与「搜索」刻意不同：不套 ignore/硬排除/点目录规则（那是搜索器的口径），
/// 列目录是直述——目录里有什么就有什么，由调用方决定显隐（界面按开关过滤，Read 全列）。
/// </summary>
public static class DirectoryLister
{
    /// <summary>
    /// 一次列出 <paramref name="root"/> 下的一层子项（不递归），按枚举顺序收满即停。
    /// 界面浏览态用它：只需「这一层有什么」的一屏，目录被文件挤掉也无妨，反正能再点进去。
    /// </summary>
    /// <param name="root">目录绝对路径（调用方已确认存在）</param>
    /// <param name="maxItems">最多收集多少条（按 <paramref name="include"/> 过滤后的条数算）；
    /// 没额度视为「什么都还没看」，直接返回空并标 <see cref="DirectoryList.HasMore"/></param>
    /// <param name="ct">取消令牌</param>
    /// <param name="include">可选过滤，在收集前应用（与上限配合时语义一致：过滤掉的条目不占名额，也不顶出 HasMore）</param>
    /// <returns>收集到的条目与「还有更多」标记</returns>
    /// <exception cref="UnauthorizedAccessException">目录本身不可枚举时抛出，由外壳决定怎么说</exception>
    /// <exception cref="IOException">同上（含目录被并发删除）</exception>
    public static DirectoryList List(string root, int maxItems, CancellationToken ct, Func<DirectoryEntry, bool>? include = null)
    {
        List<DirectoryEntry> list = new();
        if (maxItems <= 0) return new DirectoryList(list, HasMore: true); // 没额度 = 一条都没看，按「还有更多」算

        foreach (DirectoryEntry entry in Enumerate(root, ct))
        {
            if (include != null && !include(entry)) continue;

            // 额度判定必须在过滤之后：只收一类（include 滤掉另一类）时若按未过滤的条数判，
            // 恰好收满、后面只剩另一类，也会误报「还有更多」
            if (list.Count >= maxItems) return new DirectoryList(list, HasMore: true); // 已够一屏，不再收拾后面的（也不数全量）

            list.Add(entry);
        }

        return new DirectoryList(list, HasMore: false);
    }

    /// <summary>
    /// 一次列出 <paramref name="root"/> 下的一层子项，<b>额度先给目录、文件用剩下的</b>。
    /// Read 工具用它：枚举按名字走，目录名若排在文件后面，收满上限就停会把子目录整个裁掉
    /// （实测 160 个文件 + 一个 zdir，按枚举序取前 150 条，里面 0 个目录）——而 Read 是脑
    /// （ADR 0082）唯一的目录入口，看不见子目录就无路可走。
    ///
    /// 只遍历一遍：目录池与文件池各收各的，遍历结束后再按「目录优先」把文件裁到剩余额度。
    /// 选一遍不是因为快（两遍写法的第一遍同样要扫完整层，实测两者在噪声内），
    /// 而是因为路径只有一条，没有「第二遍要不要再走」的分支。
    /// 代价是必须扫完这一层才知道后面还有没有目录（这是「目录不许漏」的必然代价，
    /// 与「收满就停」不可兼得），条目总量仍是 <paramref name="maxItems"/>。
    /// </summary>
    /// <param name="root">目录绝对路径（调用方已确认存在）</param>
    /// <param name="maxItems">条目总上限，目录先占、文件用剩下的</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>目录与文件两池，各带「还有更多」</returns>
    /// <exception cref="UnauthorizedAccessException">目录本身不可枚举时抛出，由外壳决定怎么说</exception>
    /// <exception cref="IOException">同上（含目录被并发删除）</exception>
    public static DirectoryFirstListing ListDirectoriesFirst(string root, int maxItems, CancellationToken ct)
    {
        List<DirectoryEntry> directories = new();
        List<DirectoryEntry> files = new();
        bool hasMoreDirectories = false;
        bool hasMoreFiles = false;
        if (maxItems <= 0) return new DirectoryFirstListing(directories, true, files, true);

        foreach (DirectoryEntry entry in Enumerate(root, ct))
        {
            if (entry.IsDirectory)
            {
                if (directories.Count < maxItems)
                {
                    directories.Add(entry);
                    continue;
                }

                // 目录池满 = 文件额度必为 0，不必再看后面有什么文件
                hasMoreDirectories = true;
                break;
            }

            if (directories.Count >= maxItems)
            {
                // 额度已被目录吃满，文件不列：这一个就是「没列出来的文件」，得记下来，
                // 否则外壳只能拿「目录池满」反推，会把「根本没有文件」也报成「文件未列出」
                hasMoreFiles = true;
                continue;
            }

            if (files.Count < maxItems) files.Add(entry);
            else hasMoreFiles = true;
        }

        // 目录优先的兑现：文件只留剩下的额度（目录可能排在文件之后才被枚举到）
        int fileKeep = Math.Max(0, maxItems - directories.Count);
        if (files.Count > fileKeep)
        {
            files.RemoveRange(fileKeep, files.Count - fileKeep);
            hasMoreFiles = true;
        }

        return new DirectoryFirstListing(directories, hasMoreDirectories, files, hasMoreFiles);
    }

    /// <summary>单层遍历：坏条目（竞态删除／无权限）跳过，不因一条坏而毁掉整个列表</summary>
    private static IEnumerable<DirectoryEntry> Enumerate(string root, CancellationToken ct)
    {
        foreach (FileSystemInfo info in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            ct.ThrowIfCancellationRequested();
            if (TryToEntry(info) is { } entry) yield return entry;
        }
    }

    private static DirectoryEntry? TryToEntry(FileSystemInfo info)
    {
        try
        {
            return ToEntry(info);
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException)
        {
            return null;
        }
    }

    private static DirectoryEntry ToEntry(FileSystemInfo info)
    {
        bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
        bool isHidden = info.Name.StartsWith('.') || (info.Attributes & FileAttributes.Hidden) != 0;
        return new DirectoryEntry(
            info.FullName,
            info.Name,
            isDir,
            isDir ? 0 : info is FileInfo file ? file.Length : 0,
            isHidden,
            (info.Attributes & FileAttributes.System) != 0);
    }
}
