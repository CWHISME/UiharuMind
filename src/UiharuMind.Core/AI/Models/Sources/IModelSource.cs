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
public sealed record ModelRepoSummary(string Repository, long Downloads);

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
    /// <param name="limit">条数上限</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>按下载量排序的仓库</returns>
    Task<IReadOnlyList<ModelRepoSummary>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// 列出仓库里所有文件（递归）
    /// </summary>
    /// <param name="repository">owner/repo</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>文件列表</returns>
    Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken cancellationToken);

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
    /// <returns>下载请求</returns>
    DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath);
}
