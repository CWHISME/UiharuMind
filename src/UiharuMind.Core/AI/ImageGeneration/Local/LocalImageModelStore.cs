using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 本地生图模型组件的下载与删除。组件按仓库落进模型目录（与 GGUF 下载同一布局、同一份清单），清单里标「只作组件」；
/// 同一个文件只存一份、几个生图模型共用，删的时候只删没有别的生图模型在用、没有还在下的档位要用、也不是用户当对话模型下的
/// </summary>
/// <param name="downloader">按仓库排下载</param>
/// <param name="modelRoot">模型目录</param>
/// <param name="source">下载源</param>
/// <param name="models">生图模型列表（看还有谁在用）</param>
/// <param name="catalog">可下载列表（看还在下的档位要用哪些组件）；null 用内置的</param>
/// <param name="release">删文件前放开引擎（在跑的正是它就停）；null 不管</param>
public sealed class LocalImageModelStore(
    ModelRepoDownloader downloader,
    Func<string> modelRoot,
    Func<IModelSource> source,
    Func<IReadOnlyList<ImageModelInfo>> models,
    ImageModelCatalog? catalog = null,
    Action<SdCppModelSetup>? release = null)
{
    /// <summary>
    /// 走全局队列、当前模型目录与下载源的那一个
    /// </summary>
    public static LocalImageModelStore Shared { get; } = new(ModelRepoDownloader.Shared,
        () => LocalImageModels.ModelRoot, () => ModelSources.CreateForHuggingFaceRepos(),
        () => ImageModelSettingConfig.Current.Models, release: setup => SdCppEngine.Shared.Release(setup));

    /// <summary>
    /// 组件在本机的落盘路径
    /// </summary>
    /// <param name="component">组件</param>
    /// <returns>路径</returns>
    public string PathOf(ImageCatalogComponent component) => ModelRepoDownloader.FilePath(
        ModelRepoDownloader.RepoDirectory(modelRoot(), component.Repo),
        new ModelRepoFile(component.File, component.Size, component.Sha256));

    /// <summary>
    /// 这一档的组件都在本机
    /// </summary>
    /// <param name="tier">档位</param>
    public bool IsInstalled(ImageCatalogTier tier) => tier.Components.All(x => File.Exists(PathOf(x)));

    /// <summary>
    /// 把这一档缺的组件排进下载队列（已有的不排，共用的组件只下一次）
    /// </summary>
    /// <param name="tier">档位</param>
    /// <param name="onInstalled">组件到齐后调一次（在下载线程上）；本来就齐了不调，调用方自己看 <see cref="IsInstalled"/></param>
    /// <returns>排进去的下载项</returns>
    public IReadOnlyList<DownloadJob> Enqueue(ImageCatalogTier tier, Func<Task> onInstalled)
    {
        IModelSource repoSource = source();
        string root = modelRoot();
        int notified = 0;
        List<DownloadJob> jobs = [];
        foreach (ImageCatalogComponent component in tier.Components)
        {
            ModelRepoQuant file = new(Path.GetFileName(component.File), "",
                [new ModelRepoFile(component.File, component.Size, component.Sha256)]);
            jobs.AddRange(downloader.Enqueue(repoSource, component.Repo, root, file, null, async () =>
            {
                if (IsInstalled(tier) && Interlocked.Exchange(ref notified, 1) == 0)
                    await onInstalled().ConfigureAwait(false);
            }, componentOnly: true));
        }

        return jobs;
    }

    /// <summary>
    /// 删掉一个本地生图模型自己的组件：先放开引擎，再在后台删。别的生图模型在用的、还在下的档位要用的、
    /// 用户当对话模型下的、手放的都不删
    /// </summary>
    /// <param name="model">要删的生图模型（调用方随后把它移出列表）</param>
    /// <returns>删掉的文件</returns>
    public Task<IReadOnlyList<string>> DeleteAsync(ImageModelInfo model)
    {
        if (model.Local == null) return Task.FromResult<IReadOnlyList<string>>([]);
        release?.Invoke(LocalImageModels.ToSetup(model, modelRoot()));
        return Task.Run(() => DeleteComponents(model));
    }

    internal IReadOnlyList<string> DeleteComponents(ImageModelInfo model)
    {
        if (model.Local is not { } local) return [];
        string root = modelRoot();
        HashSet<string> inUse = models()
            .Where(x => !ReferenceEquals(x, model) && x.Local != null)
            .SelectMany(x => x.Local!.Components.Select(c => Path.GetFullPath(c.Resolve(root))))
            .ToHashSet(StringComparer.Ordinal);
        inUse.UnionWith(PendingTierFiles());

        List<string> deleted = [];
        foreach (LocalImageComponent component in local.Components.Where(x => x.Repository != null))
        {
            string path = Path.GetFullPath(component.Resolve(root));
            if (!inUse.Contains(path) && ModelRepoDownloader.DeleteComponent(path)) deleted.Add(path);
        }

        return deleted;
    }

    // 还在下的档位要用的组件：共用的那份多半早就在盘上，删了它，那一档下完也凑不齐
    private IEnumerable<string> PendingTierFiles()
    {
        HashSet<string> pending = downloader.PendingPaths().Select(Path.GetFullPath).ToHashSet(StringComparer.Ordinal);
        if (pending.Count == 0) return [];
        return (catalog ?? ImageModelCatalog.BuiltIn).Families
            .SelectMany(x => x.Tiers)
            .Select(x => x.Components.Select(c => Path.GetFullPath(PathOf(c))).ToList())
            .Where(x => x.Any(pending.Contains))
            .SelectMany(x => x);
    }
}
