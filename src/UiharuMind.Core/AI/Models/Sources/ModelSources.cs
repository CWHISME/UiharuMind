namespace UiharuMind.Core.AI.Models.Sources;

/// <summary>
/// 按下载源设置取模型源，并改写 GitHub 下载地址
/// </summary>
public static class ModelSources
{
    private static readonly HttpClient SharedClient = CreateClient();

    /// <summary>
    /// 当前设置对应的模型源
    /// </summary>
    /// <param name="config">下载源设置，默认取当前</param>
    /// <param name="httpClient">HTTP 客户端，默认共用一个</param>
    /// <returns>模型源</returns>
    public static IModelSource Create(DownloadSourceSettingConfig? config = null, HttpClient? httpClient = null)
    {
        config ??= DownloadSourceSettingConfig.Current;
        httpClient ??= SharedClient;
        string hfToken = config.HuggingFaceToken.Trim();
        return config.ModelSource switch
        {
            DownloadSourceSettingConfig.SourceModelScope =>
                new ModelScopeModelSource(httpClient, config.ModelScopeToken.Trim()),
            DownloadSourceSettingConfig.SourceHfMirror =>
                new HuggingFaceModelSource(httpClient, DownloadSourceSettingConfig.HfMirrorEndpoint, hfToken),
            DownloadSourceSettingConfig.SourceCustom when Uri.TryCreate(config.CustomEndpoint.Trim(), UriKind.Absolute, out _) =>
                new HuggingFaceModelSource(httpClient, config.CustomEndpoint.Trim(), hfToken),
            _ => new HuggingFaceModelSource(httpClient, DownloadSourceSettingConfig.HuggingFaceEndpoint, hfToken)
        };
    }

    /// <summary>
    /// 给 GitHub 发布包下载地址套上代理前缀；不是 GitHub 地址或没设代理原样返回
    /// </summary>
    /// <param name="url">下载地址</param>
    /// <param name="proxyPrefix">代理前缀，默认取当前设置</param>
    /// <returns>实际请求的地址</returns>
    public static string ApplyGitHubProxy(string url, string? proxyPrefix = null)
    {
        proxyPrefix = (proxyPrefix ?? DownloadSourceSettingConfig.Current.GitHubProxyPrefix).Trim();
        if (proxyPrefix.Length == 0) return url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            !uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return url;
        return proxyPrefix.TrimEnd('/') + "/" + url;
    }

    private static HttpClient CreateClient()
    {
        HttpClient client = new() { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("UiharuMind");
        return client;
    }
}
