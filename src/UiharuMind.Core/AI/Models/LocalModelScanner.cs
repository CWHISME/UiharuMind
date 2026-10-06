using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 一个扫描到的本地 GGUF
/// </summary>
/// <param name="Info">模型信息（含种类）</param>
/// <param name="IsBuiltIn">是否来自随包内置目录</param>
public sealed record LocalModelEntry(GGufModelInfo Info, bool IsBuiltIn);

/// <summary>
/// 本地模型目录的唯一扫描入口：对话、嵌入、重排同住一个目录，种类由文件头判定（ADR 0067）。
/// 已读过的文件头缓存在 <see cref="ModelSettingConfig.ModelInfos"/>，按文件名取。
/// </summary>
public static class LocalModelScanner
{
    private static readonly Lock Gate = new();

    /// <summary>
    /// 扫描内置目录与用户模型目录，同名时用户目录的覆盖内置的
    /// </summary>
    /// <param name="force">忽略缓存，重读所有文件头</param>
    /// <returns>除视觉投影外的全部本地模型</returns>
    public static IReadOnlyList<LocalModelEntry> Scan(bool force = false)
    {
        ModelSettingConfig config = ModelSettingConfig.Current;
        Dictionary<string, LocalModelEntry> entries = new(StringComparer.Ordinal);
        bool isChanged = false;
        lock (Gate)
        {
            isChanged |= ScanDirectory(config, config.DefaultLocalModelPath, true, force, entries);
            isChanged |= ScanDirectory(config, config.LocalModelPath, false, force, entries);
        }

        if (isChanged) config.Save();
        return entries.Values.ToList();
    }

    /// <summary>
    /// 只取某一种类的模型
    /// </summary>
    /// <param name="kind">模型种类</param>
    /// <param name="force">忽略缓存，重读所有文件头</param>
    /// <returns>该种类的本地模型</returns>
    public static IReadOnlyList<LocalModelEntry> Scan(ELocalModelKind kind, bool force = false)
    {
        // 读不出头的文件当对话模型列出来，加载时如实报错，好过悄悄消失
        return Scan(force).Where(x => (x.Info.Kind ?? ELocalModelKind.Chat) == kind).ToList();
    }

    private static bool ScanDirectory(
        ModelSettingConfig config,
        string directory,
        bool isBuiltIn,
        bool force,
        Dictionary<string, LocalModelEntry> entries)
    {
        if (!Directory.Exists(directory)) return false;

        bool isChanged = false;
        List<string> projectors = [];
        List<GGufModelInfo> scanned = [];
        Dictionary<string, List<string>> shardParts = new(StringComparer.Ordinal); //分片基名全路径 → 第 2 片起的文件
        HashSet<GGufModelInfo> reread = [];
        foreach (string file in Directory.GetFiles(directory, "*.gguf", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // 名字带 mmproj 的不必读头就知道是视觉投影
            if (name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
            {
                projectors.Add(file);
                continue;
            }

            if (GGufSplitName.TryParse(name, out string baseName, out int index, out _))
            {
                if (index != 1)
                {
                    AddShardPart(shardParts, file, baseName);
                    continue;
                }

                name = baseName;
            }

            if (!config.ModelInfos.TryGetValue(name, out GGufModelInfo? info))
                info = new GGufModelInfo { ModelName = name };
            if (force || info.NeedsMetadata)
            {
                info.ApplyMetadata(GGufMetadataReader.TryRead(file));
                config.ModelInfos[name] = info;
                reread.Add(info);
                isChanged = true;
            }

            info.ModelPath = file;
            if (info.Kind == ELocalModelKind.Projector)
            {
                projectors.Add(file);
                continue;
            }

            scanned.Add(info);
            entries[name] = new LocalModelEntry(info, isBuiltIn);
        }

        foreach (GGufModelInfo info in scanned)
            MergeShards(info, shardParts, reread.Contains(info));
        PairProjectors(directory, projectors, scanned);
        ApplyManifests(scanned);
        return isChanged;
    }

    private static void AddShardPart(Dictionary<string, List<string>> shardParts, string file, string baseName)
    {
        string key = Path.Combine(Path.GetDirectoryName(file) ?? "", baseName);
        if (!shardParts.TryGetValue(key, out List<string>? parts)) shardParts[key] = parts = [];
        parts.Add(file);
    }

    // 分片模型以第一片为代表：体积是所有分片之和（内存风险评估靠它），参数量也要把其余分片的张量加上
    private static void MergeShards(GGufModelInfo info, Dictionary<string, List<string>> shardParts, bool wasReread)
    {
        string key = Path.Combine(Path.GetDirectoryName(info.ModelPath) ?? "", info.ModelName);
        if (!shardParts.TryGetValue(key, out List<string>? parts)) return;

        ulong size = (ulong)new FileInfo(info.ModelPath).Length;
        foreach (string part in parts)
        {
            size += (ulong)new FileInfo(part).Length;
            if (!wasReread) continue;
            try
            {
                info.ParameterCount += GGufHeaderReader.Read(part).ParameterCount;
            }
            catch (InvalidDataException)
            {
                // 下了一半的分片读不出头，参数量少算一点不影响使用
            }
        }

        info.FileSizeBytes = size;
    }

    // 清单里写明的视觉投影优先于按目录猜
    private static void ApplyManifests(List<GGufModelInfo> models)
    {
        foreach (IGrouping<string, GGufModelInfo> group in models
                     .GroupBy(x => Path.GetDirectoryName(x.ModelPath) ?? "", StringComparer.Ordinal))
        {
            ModelManifest? manifest = ModelManifest.TryLoad(group.Key);
            if (manifest == null) continue;
            foreach (GGufModelInfo model in group)
            {
                string? projector = manifest.FindModel(Path.GetFileName(model.ModelPath))?.Projector;
                if (string.IsNullOrEmpty(projector)) continue;
                string projectorPath = Path.Combine(group.Key, projector);
                if (File.Exists(projectorPath)) model.ModelProjPath = projectorPath;
            }
        }
    }

    // 目录里只有一个视觉投影时配给同目录的对话模型。
    // 模型目录根下常是手放的杂项，只有它独占这一个对话模型时才配，免得配错导致加载失败
    private static void PairProjectors(string root, List<string> projectors, List<GGufModelInfo> models)
    {
        Dictionary<string, string?> projectorByDirectory = projectors
            .GroupBy(x => Path.GetDirectoryName(x) ?? "", StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Count() == 1 ? x.First() : null, StringComparer.Ordinal);
        string rootDirectory = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);

        foreach (IGrouping<string, GGufModelInfo> group in models
                     .Where(x => (x.Kind ?? ELocalModelKind.Chat) == ELocalModelKind.Chat)
                     .GroupBy(x => Path.GetDirectoryName(x.ModelPath) ?? "", StringComparer.Ordinal))
        {
            projectorByDirectory.TryGetValue(group.Key, out string? projector);
            bool isRoot = string.Equals(Path.GetFullPath(group.Key).TrimEnd(Path.DirectorySeparatorChar),
                rootDirectory, StringComparison.Ordinal);
            if (isRoot && group.Count() > 1) projector = null;
            foreach (GGufModelInfo model in group)
                model.ModelProjPath = projector ?? "";
        }
    }
}
