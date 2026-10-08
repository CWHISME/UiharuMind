using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 装了的 audio.cpp 模型包：下载（走全局下载队列与用户选的模型源）、判断装没装、删除。
/// 落盘到 &lt;模型目录&gt;/audio.cpp/&lt;target_directory&gt;，与官方下载器同一布局；
/// 下齐后写官方格式的包标记 .audiocpp-package-&lt;id&gt;.json，多记一份本地文件清单供删除用
/// </summary>
/// <param name="queue">下载队列</param>
/// <param name="modelRoot">取模型目录</param>
/// <param name="sourceFor">按包取模型源与实际仓库</param>
public sealed class AudioCppPackageStore(
    DownloadQueue queue,
    Func<string> modelRoot,
    Func<AudioCppPackage, AudioCppPackageSource> sourceFor)
{
    /// <summary>
    /// 包目录在模型目录下的根
    /// </summary>
    public const string RootName = "audio.cpp";

    private const string MarkerPrefix = ".audiocpp-package-";
    private const string LocalFilesKey = "uiharu_local_files";
    private static readonly Lock MarkerGate = new();

    private readonly ConcurrentDictionary<string, Task<IReadOnlyList<ModelRepoFile>>> _remoteFiles = new();

    /// <summary>
    /// 走全局队列、当前模型目录与下载源设置
    /// </summary>
    public static AudioCppPackageStore Shared { get; } = new(DownloadQueue.Shared,
        () => ModelSettingConfig.Current.LocalModelPath,
        AudioCppPackageSource.FromSettings);

    /// <summary>
    /// 包在本地的目录
    /// </summary>
    public string PackageDirectory(AudioCppPackage package)
    {
        return Path.Combine(modelRoot(), RootName, package.TargetDirectory.Replace('/', Path.DirectorySeparatorChar));
    }

    /// <summary>
    /// 装齐了没有：包标记在、标记里记的文件都在
    /// </summary>
    public bool IsInstalled(AudioCppPackage package)
    {
        string directory = PackageDirectory(package);
        IReadOnlyList<string>? files = ReadLocalFiles(MarkerPath(directory, package.Id));
        return files != null && files.All(x => File.Exists(Path.Combine(directory, x)));
    }

    /// <summary>
    /// 包里各文件的大小与校验值（同一仓库只列一次）
    /// </summary>
    /// <param name="package">包</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>包里的远端文件；仓库里缺的不列</returns>
    public async Task<IReadOnlyList<ModelRepoFile>> GetFilesAsync(AudioCppPackage package,
        CancellationToken cancellationToken = default)
    {
        AudioCppPackageSource source = sourceFor(package);
        string key = $"{source.Source.Id}|{source.Repository}|{source.Revision}";
        Task<IReadOnlyList<ModelRepoFile>> listing = _remoteFiles.GetOrAdd(key,
            _ => source.Source.ListFilesAsync(source.Repository, CancellationToken.None, source.Revision));
        IReadOnlyList<ModelRepoFile> all;
        try
        {
            all = await listing.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch when (listing.IsFaulted)
        {
            _remoteFiles.TryRemove(key, out _); //失败不缓存，下次重试
            throw;
        }

        Dictionary<string, ModelRepoFile> byPath = all.ToDictionary(x => x.Path, StringComparer.Ordinal);
        return package.Files.Where(byPath.ContainsKey).Select(x => byPath[x]).ToList();
    }

    /// <summary>
    /// 排队下载一个包；已有且大小对得上的文件不排
    /// </summary>
    /// <param name="package">包</param>
    /// <param name="onInstalled">全部文件齐了、标记写好之后调一次（在下载线程上）</param>
    /// <param name="cancellationToken">取消（只管列文件这一步）</param>
    /// <returns>排进去的下载项；全都已有时为空</returns>
    /// <exception cref="InvalidDataException">仓库里缺了 spec 列的文件，或文件路径不安全</exception>
    public async Task<IReadOnlyList<DownloadJob>> EnqueueAsync(AudioCppPackage package, Func<Task>? onInstalled = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ModelRepoFile> files = await GetFilesAsync(package, cancellationToken).ConfigureAwait(false);
        if (files.Count != package.Files.Count)
            throw new InvalidDataException($"Package '{package.Id}' is incomplete in the repository.");

        string directory = PackageDirectory(package);
        List<(ModelRepoFile Remote, string Local)> targets = [];
        foreach (ModelRepoFile file in files)
        {
            string local = package.LocalPath(file.Path)
                           ?? throw new InvalidDataException($"Unsafe file path in package '{package.Id}': {file.Path}");
            targets.Add((file, local));
        }

        AudioCppPackageSource source = sourceFor(package);
        int finished = 0;
        List<DownloadJob> jobs = [];
        foreach ((ModelRepoFile remote, string local) in targets)
        {
            string path = Path.Combine(directory, local);
            if (IsComplete(path, remote.Size)) continue;
            jobs.Add(queue.Enqueue(Path.GetFileName(path),
                source.Source.CreateDownload(source.Repository, remote, path, source.Revision),
                async _ =>
                {
                    if (!targets.All(x => IsComplete(Path.Combine(directory, x.Local), x.Remote.Size))) return;
                    if (Interlocked.Exchange(ref finished, 1) != 0) return;
                    WriteMarker(directory, package, source, targets);
                    if (onInstalled != null) await onInstalled().ConfigureAwait(false);
                }));
        }

        if (jobs.Count == 0)
        {
            WriteMarker(directory, package, source, targets);
            if (onInstalled != null) await onInstalled().ConfigureAwait(false);
        }

        return jobs;
    }

    /// <summary>
    /// 删掉一个装好的包：标记里记的文件、标记本身，以及删空了的目录
    /// </summary>
    /// <param name="package">包</param>
    /// <returns>实际删掉的文件</returns>
    public IReadOnlyList<string> Delete(AudioCppPackage package)
    {
        return DeleteByMarker(PackageDirectory(package), MarkerPath(PackageDirectory(package), package.Id));
    }

    /// <summary>
    /// 按扫描到的模型文件删它所在的包（模型列表里点删除走这条）；没有包标记的只删这个文件
    /// </summary>
    /// <param name="modelPath">gguf 文件</param>
    /// <returns>实际删掉的文件</returns>
    public IReadOnlyList<string> DeleteContaining(string modelPath)
    {
        string fullPath = Path.GetFullPath(modelPath);
        string root = Path.GetFullPath(Path.Combine(modelRoot(), RootName)) + Path.DirectorySeparatorChar;
        // 伴生文件可能在子目录里，标记在包目录：从文件所在目录往上找，不出 audio.cpp 根
        for (string? directory = Path.GetDirectoryName(fullPath);
             directory != null && (directory + Path.DirectorySeparatorChar).StartsWith(root, StringComparison.Ordinal);
             directory = Path.GetDirectoryName(directory))
        {
            foreach (string marker in Directory.EnumerateFiles(directory, MarkerPrefix + "*.json"))
            {
                IReadOnlyList<string>? files = ReadLocalFiles(marker);
                if (files?.Any(x => Path.GetFullPath(Path.Combine(directory, x)) == fullPath) == true)
                    return DeleteByMarker(directory, marker);
            }
        }

        if (!File.Exists(fullPath)) return [];
        File.Delete(fullPath);
        return [fullPath];
    }

    private IReadOnlyList<string> DeleteByMarker(string directory, string marker)
    {
        IReadOnlyList<string> files = ReadLocalFiles(marker) ?? [];
        List<string> deleted = [];
        foreach (string path in files.Select(x => Path.Combine(directory, x)).Append(marker))
        {
            if (!File.Exists(path)) continue;
            File.Delete(path);
            deleted.Add(path);
        }

        RemoveEmptyDirectories(directory);
        return deleted;
    }

    // 从包目录往上删空目录，到 audio.cpp 根为止（根本身留着）
    private void RemoveEmptyDirectories(string directory)
    {
        string root = Path.GetFullPath(Path.Combine(modelRoot(), RootName));
        foreach (string sub in Directory.Exists(directory)
                     ? Directory.EnumerateDirectories(directory, "*", SearchOption.AllDirectories)
                         .OrderByDescending(x => x.Length).ToList()
                     : [])
        {
            if (!Directory.EnumerateFileSystemEntries(sub).Any()) Directory.Delete(sub);
        }

        for (string? current = Path.GetFullPath(directory);
             current != null && current.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal);
             current = Path.GetDirectoryName(current))
        {
            if (!Directory.Exists(current) || Directory.EnumerateFileSystemEntries(current).Any()) break;
            Directory.Delete(current);
        }
    }

    private static bool IsComplete(string path, long size)
    {
        return File.Exists(path) && (size <= 0 || new FileInfo(path).Length == size);
    }

    private static string MarkerPath(string directory, string packageId)
    {
        return Path.Combine(directory, $"{MarkerPrefix}{packageId}.json");
    }

    private static IReadOnlyList<string>? ReadLocalFiles(string marker)
    {
        if (!File.Exists(marker)) return null;
        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(marker));
            return root?[LocalFilesKey]?.AsArray()
                .Select(x => x?.GetValue<string>())
                .OfType<string>()
                .Select(x => x.Replace('/', Path.DirectorySeparatorChar))
                .ToList();
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    // 形状照官方 model_manager 的标记（schema_version 1），多一个本地文件清单
    private static void WriteMarker(string directory, AudioCppPackage package, AudioCppPackageSource source,
        IReadOnlyList<(ModelRepoFile Remote, string Local)> targets)
    {
        JsonObject files = new();
        foreach ((ModelRepoFile remote, _) in targets)
            files[remote.Path] = new JsonObject { ["size"] = remote.Size, ["etag"] = remote.Sha256 ?? "" };
        JsonObject marker = new()
        {
            ["schema_version"] = 1,
            ["package_id"] = package.Id,
            ["repo"] = source.Repository,
            ["requested_revision"] = source.Revision ?? "",
            ["resolved_revision"] = source.Revision ?? "",
            ["installed_at_unix"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["files"] = files,
            [LocalFilesKey] = new JsonArray(targets.Select(x => (JsonNode)x.Local.Replace(Path.DirectorySeparatorChar, '/')).ToArray())
        };

        lock (MarkerGate)
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(MarkerPath(directory, package.Id),
                marker.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}

/// <summary>
/// 一个包实际从哪下
/// </summary>
/// <param name="Source">模型源</param>
/// <param name="Repository">实际仓库（可能是镜像）</param>
/// <param name="Revision">分支或提交；空为模型源的默认分支</param>
public sealed record AudioCppPackageSource(IModelSource Source, string Repository, string? Revision)
{
    /// <summary>
    /// 官方仓库在 ModelScope 上的镜像（audio.cpp README 给的）
    /// </summary>
    private static readonly Dictionary<string, string> ModelScopeMirrors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audio-cpp/audio.cpp-gguf"] = "HereIsMark/audio.cpp-gguf"
    };

    /// <summary>
    /// 按下载源设置决定：选了 ModelScope 时官方仓库走镜像，没有镜像的 HF 包退到 hf-mirror（选 ModelScope 多半连不上 HF）；
    /// 本来就在 ModelScope 上的包始终走 ModelScope
    /// </summary>
    /// <param name="package">包</param>
    /// <returns>下载来源</returns>
    public static AudioCppPackageSource FromSettings(AudioCppPackage package)
    {
        return Resolve(package, DownloadSourceSettingConfig.Current);
    }

    /// <summary>
    /// 按给定的下载源设置决定
    /// </summary>
    public static AudioCppPackageSource Resolve(AudioCppPackage package, DownloadSourceSettingConfig settings)
    {
        bool userOnModelScope = settings.ModelSource == DownloadSourceSettingConfig.SourceModelScope;
        if (package.Host == EAudioCppPackageHost.ModelScope)
            return new AudioCppPackageSource(ModelSources.Create(WithSource(settings, DownloadSourceSettingConfig.SourceModelScope)),
                package.Repository, package.Revision);
        if (!userOnModelScope)
            return new AudioCppPackageSource(ModelSources.Create(settings), package.Repository, package.Revision);
        // 镜像的提交号和 HF 对不上，只能跟默认分支
        if (ModelScopeMirrors.TryGetValue(package.Repository, out string? mirror))
            return new AudioCppPackageSource(ModelSources.Create(settings), mirror, null);
        return new AudioCppPackageSource(ModelSources.Create(WithSource(settings, DownloadSourceSettingConfig.SourceHfMirror)),
            package.Repository, package.Revision);
    }

    private static DownloadSourceSettingConfig WithSource(DownloadSourceSettingConfig settings, string source)
    {
        return new DownloadSourceSettingConfig
        {
            ModelSource = source,
            HuggingFaceToken = settings.HuggingFaceToken,
            ModelScopeToken = settings.ModelScopeToken,
            GitHubProxyPrefix = settings.GitHubProxyPrefix
        };
    }
}
