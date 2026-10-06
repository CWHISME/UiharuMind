using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 把仓库里的一个量化（连同视觉投影）排进下载队列：落到 &lt;模型目录&gt;/&lt;作者&gt;/&lt;仓库&gt;/，
/// 文件平铺在仓库目录下（扫描器按目录找清单、按文件名对条目），每个文件下完立刻记进清单
/// </summary>
/// <param name="queue">下载队列</param>
public sealed class ModelRepoDownloader(DownloadQueue queue)
{
    private static readonly Lock ManifestGate = new();

    /// <summary>
    /// 走全局队列
    /// </summary>
    public static ModelRepoDownloader Shared { get; } = new(DownloadQueue.Shared);

    /// <summary>
    /// 仓库在本地的目录
    /// </summary>
    /// <param name="modelRoot">模型目录</param>
    /// <param name="repository">owner/repo</param>
    /// <returns>目录路径</returns>
    public static string RepoDirectory(string modelRoot, string repository)
    {
        return Path.Combine([modelRoot, ..repository.Split('/', StringSplitOptions.RemoveEmptyEntries)]);
    }

    /// <summary>
    /// 仓库文件的落盘路径
    /// </summary>
    /// <param name="repoDirectory">仓库目录</param>
    /// <param name="file">仓库文件</param>
    /// <returns>文件路径</returns>
    public static string FilePath(string repoDirectory, ModelRepoFile file)
    {
        return Path.Combine(repoDirectory, Path.GetFileName(file.Path));
    }

    /// <summary>
    /// 排队下载一个量化；本地已有的文件不排
    /// </summary>
    /// <param name="source">模型源</param>
    /// <param name="repository">owner/repo</param>
    /// <param name="modelRoot">模型目录</param>
    /// <param name="quant">主模型</param>
    /// <param name="projector">一起下的视觉投影，不要为 null</param>
    /// <param name="onCompleted">全部文件齐了之后调一次（在下载线程上）</param>
    /// <returns>排进去的下载项；全都已有时为空</returns>
    public IReadOnlyList<DownloadJob> Enqueue(
        IModelSource source,
        string repository,
        string modelRoot,
        ModelRepoQuant quant,
        ModelRepoQuant? projector,
        Func<Task>? onCompleted = null)
    {
        string directory = RepoDirectory(modelRoot, repository);
        List<ModelRepoFile> files = [..quant.Files, ..projector?.Files ?? []];
        int completed = 0;
        List<DownloadJob> jobs = [];
        foreach (ModelRepoFile file in files)
        {
            string path = FilePath(directory, file);
            if (File.Exists(path)) continue;
            jobs.Add(queue.Enqueue(Path.GetFileName(path), source.CreateDownload(repository, file, path),
                async _ =>
                {
                    RecordInManifest(source.Id, repository, directory, quant, projector);
                    if (onCompleted == null || !files.All(x => File.Exists(FilePath(directory, x)))) return;
                    if (Interlocked.Exchange(ref completed, 1) == 0) await onCompleted().ConfigureAwait(false);
                }));
        }

        return jobs;
    }

    // 以磁盘为准重写这个量化的条目：已落盘的文件才记，分片按片序
    private static void RecordInManifest(string sourceId, string repository, string directory,
        ModelRepoQuant quant, ModelRepoQuant? projector)
    {
        lock (ManifestGate)
        {
            ModelManifest manifest = ModelManifest.TryLoad(directory) ??
                                     new ModelManifest { Source = sourceId, Repository = repository };
            HashSet<string> names = quant.Files.Select(x => Path.GetFileName(x.Path)).ToHashSet();
            // 首片还没落盘时条目的第一项不是首片，按任一文件认
            ManifestModel? model = manifest.Models.FirstOrDefault(x => x.Files.Any(f => names.Contains(f.Path)));
            if (model == null)
            {
                model = new ManifestModel();
                manifest.Models.Add(model);
            }

            model.Files = Downloaded(directory, quant).ToList();
            if (projector != null)
            {
                model.Projector = Path.GetFileName(projector.Files[0].Path);
                foreach (ManifestFile file in Downloaded(directory, projector))
                {
                    if (manifest.Projectors.All(x => x.Path != file.Path)) manifest.Projectors.Add(file);
                }
            }
            manifest.Save(directory);
        }
    }

    private static IEnumerable<ManifestFile> Downloaded(string directory, ModelRepoQuant quant)
    {
        return quant.Files
            .Where(x => File.Exists(FilePath(directory, x)))
            .Select(x => new ManifestFile
            {
                Path = Path.GetFileName(x.Path),
                Size = x.Size,
                Sha256 = x.Sha256?.ToLowerInvariant() ?? ""
            });
    }
}
