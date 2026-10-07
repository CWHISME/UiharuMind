namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// 站点清理器注册表。按 URL 逐个询问,第一个受理的负责清理。
/// 新增站点:实现 <see cref="ISitePageCleaner"/> 并在 <see cref="Cleaners"/> 加一行。
/// </summary>
internal static class SitePageCleaners
{
    private static readonly ISitePageCleaner[] Cleaners = [new GitHubPageCleaner()];

    /// <summary>
    /// 按 URL 挑选清理器处理正文;没有站点受理时原样返回
    /// </summary>
    /// <param name="url">目标地址</param>
    /// <param name="text">抓取到的正文(markdown)</param>
    /// <returns>清理后的正文</returns>
    public static string Clean(string url, string text)
    {
        foreach (ISitePageCleaner cleaner in Cleaners)
        {
            if (cleaner.CanClean(url))
                return cleaner.Clean(text);
        }

        return text;
    }
}
