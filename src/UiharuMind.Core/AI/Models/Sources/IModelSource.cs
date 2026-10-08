using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 仓库里的一个文件
/// </summary>
/// <param name="Path">仓库内路径</param>
/// <param name="Size">字节数</param>
/// <param name="Sha256">sha256，模型源没给为 null</param>
public sealed record ModelRepoFile(string Path, long Size, string? Sha256);

/// <summary>
/// 搜索结果里的一个仓库
/// </summary>
/// <param name="Repository">owner/repo</param>
/// <param name="Downloads">下载量</param>
/// <param name="Likes">点赞（魔搭是收藏）数</param>
/// <param name="LastModified">最后更新时间，模型源没给为 null</param>
/// <param name="CreatedAt">创建时间，模型源没给为 null</param>
public sealed record ModelRepoSummary(string Repository, long Downloads, long Likes = 0,
    DateTimeOffset? LastModified = null, DateTimeOffset? CreatedAt = null);

/// <summary>
/// 搜索排序
/// </summary>
public enum EModelSearchSort
{
    Downloads,
    Trending,
    Likes,
    Updated,
    Created
}

/// <summary>
/// 搜索结果的一页
/// </summary>
/// <param name="Items">这一页的仓库</param>
/// <param name="NextCursor">下一页的游标（各模型源自定，原样传回）；没有更多为 null</param>
public sealed record ModelSearchPage(IReadOnlyList<ModelRepoSummary> Items, string? NextCursor);

/// <summary>
/// 模型源：从哪拿模型文件（见 CONTEXT「模型源」）。只管拿文件，不参与问话
/// </summary>
public interface IModelSource
{
    /// <summary>
    /// 写进模型清单的来源 Id。HF 与其镜像同属 huggingface——仓库命名空间是同一个
    /// </summary>
    string Id { get; }

    /// <summary>
    /// 搜索仓库（尽量只要带 GGUF 的）
    /// </summary>
    /// <param name="query">关键词</param>
    /// <param name="sort">排序</param>
    /// <param name="limit">每页条数</param>
    /// <param name="cursor">上一页给的 <see cref="ModelSearchPage.NextCursor"/>；第一页为 null</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>按 <paramref name="sort"/> 排好的一页仓库</returns>
    Task<ModelSearchPage> SearchAsync(string query, EModelSearchSort sort, int limit, string? cursor,
        CancellationToken cancellationToken);

    /// <summary>
    /// 列出仓库里所有文件（递归）
    /// </summary>
    /// <param name="repository">owner/repo</param>
    /// <param name="cancellationToken">取消</param>
    /// <param name="revision">分支或提交；空为模型源的默认分支</param>
    /// <returns>文件列表</returns>
    Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken cancellationToken,
        string? revision = null);

    /// <summary>
    /// 仓库的说明（README.md 原文）
    /// </summary>
    /// <param name="repository">owner/repo</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>说明原文；仓库没有说明为 null</returns>
    Task<string?> GetReadmeAsync(string repository, CancellationToken cancellationToken);

    /// <summary>
    /// 某个文件的下载请求（地址、凭据、校验值）
    /// </summary>
    /// <param name="repository">owner/repo</param>
    /// <param name="file">仓库里的文件</param>
    /// <param name="destinationPath">落盘路径</param>
    /// <param name="revision">分支或提交；空为模型源的默认分支</param>
    /// <returns>下载请求</returns>
    DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath,
        string? revision = null);
}
