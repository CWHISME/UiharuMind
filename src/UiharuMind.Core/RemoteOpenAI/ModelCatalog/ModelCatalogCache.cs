using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.RemoteOpenAI.ModelCatalog;

/// <summary>模型目录缓存的读写契约</summary>
public interface IModelCatalogCache
{
    /// <summary>读指定目录地址的缓存；无缓存或坏文件返回 null</summary>
    IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl);

    /// <summary>覆盖写入指定目录地址的缓存</summary>
    void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries);
}

/// <summary>
/// 模型目录的本地缓存：按目录地址哈希分文件，落在 AppPaths.Cache.ModelCatalogs。
/// 语义与 McpToolCatalogs 一致：可再生、用户可随手删、离线可读（ADR 0092）。
/// </summary>
public sealed class ModelCatalogCache : IModelCatalogCache
{
    private readonly string _root;

    public ModelCatalogCache(string? root = null)
    {
        _root = root ?? AppPaths.Cache.ModelCatalogs;
    }

    public IReadOnlyList<ModelCatalogEntry>? TryRead(string catalogUrl)
    {
        return SaveUtility.Load<List<ModelCatalogEntry>>(PathFor(catalogUrl), SaveUtility.SaveJsonOptions);
    }

    public void Write(string catalogUrl, IReadOnlyList<ModelCatalogEntry> entries)
    {
        SaveUtility.Save(PathFor(catalogUrl), entries.ToList(), SaveUtility.SaveJsonOptions);
    }

    /// <summary>缓存文件路径（internal 供测试钉文件名规则）</summary>
    internal string PathFor(string catalogUrl) => Path.Combine(_root, HashUrl(catalogUrl) + ".json");

    private static string HashUrl(string catalogUrl) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(catalogUrl))).ToLowerInvariant();
}
