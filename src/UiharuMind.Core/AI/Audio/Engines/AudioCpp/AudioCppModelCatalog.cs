using System.Text.Json;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 模型包放在哪种仓库
/// </summary>
public enum EAudioCppPackageHost
{
    HuggingFace,
    ModelScope
}

/// <summary>
/// model_specs 里的一个可下载 GGUF 包（一个家族的某个语种 / 精度）
/// </summary>
/// <param name="Id">包 id，如 pocket_tts_english_q8_0</param>
/// <param name="Family">模型家族</param>
/// <param name="Task">tts / asr</param>
/// <param name="DisplayName">包的显示名</param>
/// <param name="Precision">精度，如 q8_0</param>
/// <param name="IsDefault">是不是家族的默认包</param>
/// <param name="Status">上游成熟度：supported / experimental / community / wip…</param>
/// <param name="Languages">语种</param>
/// <param name="TargetDirectory">包目录（相对模型根）</param>
/// <param name="StripPrefix">远端路径去掉这段前缀后落在包目录下</param>
/// <param name="Files">远端文件</param>
/// <param name="Host">仓库种类</param>
/// <param name="Repository">owner/repo</param>
/// <param name="Revision">分支或提交；空为默认分支</param>
/// <param name="NeedsEspeak">要系统装 eSpeak-ng（预编译包不带）</param>
public sealed record AudioCppPackage(
    string Id,
    string Family,
    EAudioCppTask Task,
    string DisplayName,
    string Precision,
    bool IsDefault,
    string Status,
    IReadOnlyList<string> Languages,
    string TargetDirectory,
    string StripPrefix,
    IReadOnlyList<string> Files,
    EAudioCppPackageHost Host,
    string Repository,
    string? Revision,
    bool NeedsEspeak)
{
    /// <summary>
    /// 装好后扫描器看到的模型名（包里 gguf 的文件名）；包里没有 gguf 为空
    /// </summary>
    public string ModelName => Path.GetFileNameWithoutExtension(
        Files.FirstOrDefault(x => x.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)) ?? "");

    /// <summary>
    /// 远端文件在包目录下的相对路径
    /// </summary>
    /// <param name="remotePath">远端路径</param>
    /// <returns>相对路径；越出包目录为 null</returns>
    public string? LocalPath(string remotePath)
    {
        string path = remotePath;
        if (StripPrefix.Length > 0)
        {
            string prefix = StripPrefix.TrimEnd('/') + "/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal)) return null;
            path = path[prefix.Length..];
        }

        // 与官方下载器同一条安全规则：不许绝对路径、不许 ..
        if (path.Length == 0 || Path.IsPathRooted(path) || path.Split('/').Any(x => x is ".." or "")) return null;
        return path.Replace('/', Path.DirectorySeparatorChar);
    }
}

/// <summary>
/// 读引擎包自带的 model_specs/*.json，列出能下的语音合成 / 识别 GGUF 包。
/// spec 跟着引擎版本走，用装好的引擎里那份，模型与引擎就不会对不上
/// </summary>
public static class AudioCppModelCatalog
{
    private static readonly HashSet<string> SupportedKinds = ["huggingface_snapshot", "modelscope_snapshot"];

    /// <summary>
    /// 推荐的包：无系统依赖、已在本机（Metal）实测跑通。英文小模型 + 中文可用的一对
    /// </summary>
    public static IReadOnlySet<string> Recommended { get; } = new HashSet<string>
    {
        "pocket_tts_english_q8_0",
        "moonshine_streaming_tiny_q8_0",
        "voxcpm1_0_5b_q8_0",
        "qwen3_asr_0_6b_q8_0"
    };

    /// <summary>
    /// 读一个 model_specs 目录里的全部包
    /// </summary>
    /// <param name="specsDirectory">目录</param>
    /// <returns>包列表；目录不存在为空</returns>
    public static IReadOnlyList<AudioCppPackage> Load(string specsDirectory) =>
        LoadFamilies(specsDirectory).SelectMany(x => x.Packages).ToList();

    /// <summary>
    /// 读一个 model_specs 目录，按家族分组
    /// </summary>
    /// <param name="specsDirectory">目录</param>
    /// <returns>有可下 GGUF 包的家族；目录不存在为空</returns>
    public static IReadOnlyList<AudioCppFamily> LoadFamilies(string specsDirectory)
    {
        if (!Directory.Exists(specsDirectory)) return [];
        List<AudioCppFamily> families = [];
        foreach (string file in Directory.EnumerateFiles(specsDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                if (ParseFamily(File.ReadAllText(file)) is { Packages.Count: > 0 } family) families.Add(family);
            }
            catch (Exception e) when (e is JsonException or IOException or InvalidOperationException)
            {
                Log.Warning($"Skip audio.cpp model spec {Path.GetFileName(file)}: {e.Message}");
            }
        }

        return families;
    }

    /// <summary>
    /// 解析一份 spec 里的包
    /// </summary>
    /// <param name="json">spec 原文</param>
    /// <returns>包列表</returns>
    public static IReadOnlyList<AudioCppPackage> Parse(string json) => ParseFamily(json)?.Packages ?? [];

    /// <summary>
    /// 解析一份 spec：只要 tts / asr 家族的 GGUF 包（扫描器只认 GGUF），下载方式不认识的跳过
    /// </summary>
    /// <param name="json">spec 原文</param>
    /// <returns>家族；不是 tts / asr 为 null</returns>
    public static AudioCppFamily? ParseFamily(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        EAudioCppTask? task = GetString(root, "category") switch
        {
            "tts" => EAudioCppTask.Tts,
            "asr" => EAudioCppTask.Asr,
            _ => null
        };
        if (task == null) return null;

        string family = GetString(root, "family");
        string status = GetString(root, "status");
        List<string> languages = GetStrings(root, "languages");
        bool needsEspeak = json.Contains("espeak", StringComparison.OrdinalIgnoreCase);
        JsonElement ui = root.TryGetProperty("ui", out JsonElement uiElement) ? uiElement : default;
        List<AudioCppPackage> packages = root.TryGetProperty("packages", out JsonElement packageArray)
            ? ParsePackages(root, packageArray, family, task.Value, status, languages, needsEspeak)
            : [];

        return new AudioCppFamily(
            family,
            task.Value,
            GetString(root, "display_name") is { Length: > 0 } name ? name : family,
            GetString(root, "description"),
            status,
            languages,
            GetStrings(root, "tasks"),
            GetStrings(root, "modes"),
            root.TryGetProperty("capabilities", out JsonElement capabilities) && capabilities.ValueKind == JsonValueKind.Object
                ? capabilities.EnumerateObject().SelectMany(x => GetStrings(capabilities, x.Name)).Distinct().ToList()
                : [],
            ui.ValueKind == JsonValueKind.Object ? GetStrings(ui, "builtin_voices") : [],
            ui.ValueKind == JsonValueKind.Object ? GetString(ui, "recommended_package") : "",
            needsEspeak,
            packages,
            root.TryGetProperty("options", out JsonElement options) && options.ValueKind == JsonValueKind.Object &&
            options.TryGetProperty("request", out JsonElement request) && request.ValueKind == JsonValueKind.Array
                ? request.EnumerateArray().Select(x => GetString(x, "name")).Where(x => x.Length > 0).ToList()
                : []);
    }

    private static List<AudioCppPackage> ParsePackages(JsonElement root, JsonElement packages, string family,
        EAudioCppTask task, string status, List<string> languages, bool needsEspeak)
    {
        JsonElement? defaults = root.TryGetProperty("package_defaults", out JsonElement packageDefaults) &&
                                packageDefaults.TryGetProperty("download", out JsonElement download)
            ? download
            : null;

        List<AudioCppPackage> result = [];
        foreach (JsonElement package in packages.EnumerateArray())
        {
            if (GetString(package, "format") != "gguf") continue;
            JsonElement? own = package.TryGetProperty("download", out JsonElement packageDownload) ? packageDownload : null;
            string kind = Merged(own, defaults, "kind");
            string repository = Merged(own, defaults, "repo");
            if (!SupportedKinds.Contains(kind) || repository.Length == 0) continue;

            string revision = Merged(own, defaults, "revision");
            EAudioCppPackageHost host = kind == "modelscope_snapshot" ? EAudioCppPackageHost.ModelScope : EAudioCppPackageHost.HuggingFace;
            result.Add(new AudioCppPackage(
                GetString(package, "id"),
                family,
                task,
                GetString(package, "display_name"),
                GetString(package, "precision"),
                package.TryGetProperty("default", out JsonElement isDefault) && isDefault.ValueKind == JsonValueKind.True,
                status,
                languages,
                GetString(package, "target_directory"),
                GetString(package, "strip_prefix"),
                GetStrings(package, "files"),
                host,
                repository,
                // main 是 HF 的默认分支；换到 ModelScope 镜像时默认分支是 master，交给模型源自己定
                revision is "" or "main" ? null : revision,
                needsEspeak));
        }

        return result;
    }

    private static string Merged(JsonElement? own, JsonElement? defaults, string name)
    {
        if (own is { } o && o.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String)
            return value.GetString() ?? "";
        return defaults is { } d ? GetString(d, name) : "";
    }

    private static string GetString(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";
    }

    private static List<string> GetStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out JsonElement value) || value.ValueKind != JsonValueKind.Array) return [];
        return value.EnumerateArray()
            .Where(x => x.ValueKind == JsonValueKind.String)
            .Select(x => x.GetString()!)
            .ToList();
    }
}
