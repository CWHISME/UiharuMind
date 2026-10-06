using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 不联网的模型源：搜索与列文件返回给定数据，或抛给定异常
/// </summary>
internal sealed class FakeModelSource : IModelSource
{
    public string Id => "fake";

    public IReadOnlyList<ModelRepoSummary> SearchResults { get; init; } = [];

    public IReadOnlyList<ModelRepoFile> Files { get; init; } = [];

    public Exception? ListError { get; init; }

    public string? Readme { get; init; }

    public int ReadmeCount { get; private set; }

    public int SearchCount { get; private set; }

    public string? LastQuery { get; private set; }

    public Task<IReadOnlyList<ModelRepoSummary>> SearchAsync(string query, int limit, CancellationToken token)
    {
        SearchCount++;
        LastQuery = query;
        return Task.FromResult(SearchResults);
    }

    public Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken token) =>
        ListError != null ? Task.FromException<IReadOnlyList<ModelRepoFile>>(ListError) : Task.FromResult(Files);

    public Task<string?> GetReadmeAsync(string repository, CancellationToken token)
    {
        ReadmeCount++;
        return Task.FromResult(Readme);
    }

    public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath) =>
        new(new Uri("http://127.0.0.1:1/" + file.Path), destinationPath, 1, file.Sha256);
}
