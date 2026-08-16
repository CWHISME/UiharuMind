namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

public record SearchResultItem(string Title, string Url, string Snippet);

public interface ISearchProvider
{
    string Name { get; }

    /// <summary>
    /// 本引擎当前是否可用。返回 false 表示"没配好"而非"失败",兜底链安静跳过——
    /// 需要 API key 的引擎在没填 key 时就是这种状态,日志里不该看着像出错了。
    /// </summary>
    bool IsAvailable => true;

    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct);
}