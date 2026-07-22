namespace UiharuMind.Core.AI.Agent.Tools.WebTools;

public record SearchResultItem(string Title, string Url, string Snippet);

public interface ISearchProvider
{
    string Name { get; }
    Task<IReadOnlyList<SearchResultItem>> SearchAsync(
        string query, int maxCount, CancellationToken ct);
}