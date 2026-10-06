using System.Net;
using System.Text.RegularExpressions;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 仓库说明（README）：拉取原文，整理成适合直接渲染的 markdown
/// </summary>
public static partial class ModelReadme
{
    /// <summary>
    /// 拉一份说明原文；404 视为没有
    /// </summary>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="url">README 地址</param>
    /// <param name="headers">额外请求头（如令牌）</param>
    /// <param name="cancellationToken">取消</param>
    /// <returns>原文；没有为 null</returns>
    public static async Task<string?> FetchAsync(HttpClient httpClient, string url,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Get, url);
        foreach ((string key, string value) in headers ?? new Dictionary<string, string>())
            request.Headers.TryAddWithoutValidation(key, value);
        using HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 整理模型卡：去掉开头的 YAML 元数据（渲染出来是一堆杂乱的键值），其余交给 <see cref="MarkdownCleaner"/>
    /// </summary>
    /// <param name="markdown">原文</param>
    /// <returns>整理后的 markdown</returns>
    public static string Clean(string markdown)
    {
        return MarkdownCleaner.Clean(FrontMatterPattern().Replace(markdown.Replace("\r\n", "\n"), "", 1));
    }

    [GeneratedRegex(@"\A\uFEFF?---\n.*?\n---[ \t]*(\n|\z)", RegexOptions.Singleline)]
    private static partial Regex FrontMatterPattern();
}
