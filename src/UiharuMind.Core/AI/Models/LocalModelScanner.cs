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
        foreach (string file in Directory.GetFiles(directory, "*.gguf", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            // 名字带 mmproj 的不必读头就能排除
            if (name.Contains("mmproj", StringComparison.OrdinalIgnoreCase)) continue;

            if (!config.ModelInfos.TryGetValue(name, out GGufModelInfo? info))
                info = new GGufModelInfo { ModelName = name };
            if (force || info.NeedsMetadata)
            {
                info.ApplyMetadata(GGufMetadataReader.TryRead(file));
                config.ModelInfos[name] = info;
                isChanged = true;
            }

            info.ModelPath = file;
            info.ModelProjPath = "";
            if (info.Kind == ELocalModelKind.Projector) continue;
            entries[name] = new LocalModelEntry(info, isBuiltIn);
        }

        return isChanged;
    }
}
