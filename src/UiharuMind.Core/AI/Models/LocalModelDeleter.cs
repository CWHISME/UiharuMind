namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 删除一个本地模型的文件：分片全删；下载来的（清单里有）在仓库目录里已无别的模型时，
/// 连视觉投影与清单一起删；手放的只删模型文件本身
/// </summary>
public static class LocalModelDeleter
{
    /// <summary>
    /// 删除模型文件
    /// </summary>
    /// <param name="modelPath">模型主文件（分片模型为任一片）</param>
    /// <returns>实际删掉的文件</returns>
    public static IReadOnlyList<string> Delete(string modelPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
        List<string> deleted = [];
        List<string> parts = FindParts(modelPath);
        foreach (string part in parts) DeleteFile(part, deleted);

        ModelManifest? manifest = ModelManifest.TryLoad(directory);
        if (manifest == null) return deleted;

        HashSet<string> names = parts.Select(Path.GetFileName).OfType<string>().ToHashSet(StringComparer.Ordinal);
        int removed = manifest.Models.RemoveAll(x => x.Files.Any(f => names.Contains(f.Path)));
        if (removed == 0) return deleted; //手放进仓库目录的，清单不归它管

        if (HasOtherModels(directory))
        {
            manifest.Save(directory);
            return deleted;
        }

        // 仓库目录里不剩模型了：视觉投影与清单留着也没用
        foreach (ManifestFile projector in manifest.Projectors)
            DeleteFile(Path.Combine(directory, projector.Path), deleted);
        DeleteFile(Path.Combine(directory, ModelManifest.FileName), deleted);
        if (!Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
        return deleted;
    }

    // 分片模型按基名与片数找齐同目录的所有分片
    private static List<string> FindParts(string modelPath)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(modelPath))!;
        if (!GGufSplitName.TryParse(Path.GetFileNameWithoutExtension(modelPath), out string baseName, out _, out int count))
            return [modelPath];

        return Directory.EnumerateFiles(directory, "*.gguf")
            .Where(file => GGufSplitName.TryParse(Path.GetFileNameWithoutExtension(file), out string name, out _, out int n) &&
                           name == baseName && n == count)
            .ToList();
    }

    private static bool HasOtherModels(string directory)
    {
        return Directory.EnumerateFiles(directory, "*.gguf")
            .Any(file => !Path.GetFileName(file).Contains("mmproj", StringComparison.OrdinalIgnoreCase));
    }

    private static void DeleteFile(string path, List<string> deleted)
    {
        if (!File.Exists(path)) return;
        File.Delete(path);
        deleted.Add(path);
    }
}
