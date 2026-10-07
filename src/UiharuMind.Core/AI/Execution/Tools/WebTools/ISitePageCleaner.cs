namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 站点特化的网页正文清理。按 URL 挑选,把该站特有的 UI 噪音
/// (按钮文字、导航提示、登录横幅等)从抓取的正文里剔掉。
/// 与读取器解耦:Firecrawl / Direct 哪路命中都过同一道清理。
/// </summary>
internal interface ISitePageCleaner
{
    /// <summary>
    /// 是否受理这个地址
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <returns>受理返回 true</returns>
    bool CanClean(string url);

    /// <summary>
    /// 清理正文
    /// </summary>
    /// <param name="text">抓取到的正文(markdown)</param>
    /// <returns>清理后的正文</returns>
    string Clean(string text);
}
