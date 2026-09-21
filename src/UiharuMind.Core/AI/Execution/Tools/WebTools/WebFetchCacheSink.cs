using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// WebFetch 超限全文的落盘。写到 <see cref="AppPaths.Cache.FetchedPages"/>:
/// 归 Cache 而非 Data——网页可再生(重新抓一次即可)、用户可随手删,不像 agent 产出那样清掉就留坏链。
/// </summary>
internal static class WebFetchCacheSink
{
    /// <summary>全文写盘。同名 URL 覆盖同一文件——它就是缓存,不是档案</summary>
    public static string Save(string text, string url)
    {
        string directory = AppPaths.Cache.FetchedPages;
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileNameFor(url));
        File.WriteAllText(path, text, new UTF8Encoding(false));
        return path;
    }

    /// <summary>可读前缀 + URL 稳定哈希:非法字符/超长 URL 都安全,且同一 URL 永远映射到同一文件</summary>
    internal static string FileNameFor(string url)
    {
        string prefix = new string(url.Select(c => char.IsLetterOrDigit(c) ? c : '_').Take(60).ToArray()).Trim('_');
        if (prefix.Length == 0) prefix = "page";
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8];
        return $"{prefix}_{hash}.txt";
    }
}
