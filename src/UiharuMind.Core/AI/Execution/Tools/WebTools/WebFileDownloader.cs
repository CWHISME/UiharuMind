using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Execution.Tools.WebTools;

/// <summary>
/// WebFetch 碰到非文本内容(zip/pdf/图片等)时的下载落盘。与 <see cref="WebFetchCacheSink"/> 同性质:
/// 归 Cache 而非 Data——文件可再生(重新下载即可)、用户可随手删,不是 agent 产物。
/// 流式写盘不占内存,超限即删文件按失败处理,绝不留下半个大文件。
/// </summary>
internal static class WebFileDownloader
{
    /// <summary>下载上限(字节)。超限不下载或下载中中断,避免磁盘被撑爆</summary>
    internal const long MaxDownloadBytes = 512L * 1024 * 1024;

    /// <summary>文件名里 stem 保留的最大长度(防超长文件名;hash 后缀与扩展名另算)</summary>
    private const int MaxStemLength = 80;

    /// <summary>写盘缓冲区</summary>
    private const int BufferSize = 64 * 1024;

    /// <summary>
    /// 把响应体流落盘为文件
    /// </summary>
    /// <param name="url">来源地址,用于推导文件名与稳定 hash</param>
    /// <param name="resp">已收到响应的消息,只读元数据(Content-Disposition/Content-Length),不 dispose</param>
    /// <param name="body">响应体流,由调用方打开,这里只读不 dispose</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>成功为 <see cref="PageReadResult.Exact"/>(Content 是给模型的路径说明);失败为 Fail(原因)</returns>
    public static Task<PageReadResult> DownloadAsync(
        string url, HttpResponseMessage resp, Stream body, CancellationToken ct)
    {
        return DownloadAsync(url, resp, body, AppPaths.Cache.Downloads, ct);
    }

    /// <inheritdoc cref="DownloadAsync(string,HttpResponseMessage,Stream,CancellationToken)"/>
    /// <param name="directory">落盘目录(显式入参,可单测)</param>
    internal static async Task<PageReadResult> DownloadAsync(
        string url, HttpResponseMessage resp, Stream body, string directory, CancellationToken ct)
    {
        long? declaredLength = resp.Content.Headers.ContentLength;
        if (declaredLength > MaxDownloadBytes)
        {
            return PageReadResult.Fail(
                $"file too large ({declaredLength} bytes > {MaxDownloadBytes} limit); not downloaded");
        }

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, FileNameFor(url, resp.Content.Headers.ContentDisposition?.FileName));

        long written;
        try
        {
            await using FileStream file = new(path, FileMode.Create, FileAccess.Write, FileShare.None,
                bufferSize: BufferSize, useAsync: true);
            written = await CopyCappedAsync(body, file, MaxDownloadBytes, ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            TryDelete(path);
            throw;
        }

        if (written > MaxDownloadBytes)
        {
            TryDelete(path);
            return PageReadResult.Fail($"file too large ({written} bytes > {MaxDownloadBytes} limit); download aborted");
        }

        string fileName = Path.GetFileName(path);
        Log.Debug($"[WebFetch] downloaded {fileName}: {written} bytes from {url}");
        return PageReadResult.Exact($"[Downloaded] {fileName} ({written} bytes) — saved to {path}");
    }

    /// <summary>流式拷贝并计数,读到 limit 就停;超限的部分不写盘,由调用方删文件</summary>
    internal static async Task<long> CopyCappedAsync(Stream source, Stream target, long limit, CancellationToken ct)
    {
        byte[] buffer = new byte[BufferSize];
        long total = 0;
        while (total <= limit)
        {
            int n = await source.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) break;
            total += n;
            if (total > limit) break;
            await target.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
        }

        return total;
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            //删不掉只是留下残留,不该让下载本身失败
        }
    }

    /// <summary>
    /// 由响应头里的文件名(优先)或 URL 末段推导落盘文件名:可读 stem + 稳定 hash 后缀 + 原扩展名。
    /// 稳定 hash 保证同一 URL 永远映射到同一文件(重复下载即覆盖,和 <see cref="WebFetchCacheSink"/> 同口径),
    /// 不同 URL 即使同名也不会互相覆盖。
    /// </summary>
    /// <param name="url">来源地址</param>
    /// <param name="dispositionFileName">Content-Disposition 的文件名,没有则为 null</param>
    /// <returns>安全的文件名</returns>
    internal static string FileNameFor(string url, string? dispositionFileName)
    {
        string stem = SanitizeStem(Path.GetFileNameWithoutExtension(dispositionFileName));
        string ext = SanitizeExt(Path.GetExtension(dispositionFileName));

        if (stem.Length == 0 && Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
        {
            //响应头没给文件名:从 URL 末段猜。解编码是必须的——很多下载链接的末段是 %20 之类
            string fromUrl = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
            stem = SanitizeStem(Path.GetFileNameWithoutExtension(fromUrl));
            if (ext.Length == 0) ext = SanitizeExt(Path.GetExtension(fromUrl));
        }

        if (stem.Length == 0) stem = "download";

        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..8];
        return $"{stem}_{hash}{ext}";
    }

    /// <summary>去掉路径分隔符/盘符/通配符等危险字符,防文件名穿越与注入;空串返回空</summary>
    private static string SanitizeStem(string? stem)
    {
        if (string.IsNullOrWhiteSpace(stem)) return string.Empty;
        string cleaned = new string(stem.Where(IsSafeFileNameChar).ToArray()).Trim('.', ' ');
        return cleaned.Length > MaxStemLength ? cleaned[..MaxStemLength] : cleaned;
    }

    /// <summary>扩展名只保留字母数字点,异常形态直接丢弃(连 '.exe.ps1' 这类也不放行)</summary>
    private static string SanitizeExt(string? ext)
    {
        if (string.IsNullOrWhiteSpace(ext)) return string.Empty;
        string cleaned = new string(ext.Where(IsSafeFileNameChar).ToArray());
        return cleaned.Length is 0 or > 32 ? string.Empty : cleaned;
    }

    private static bool IsSafeFileNameChar(char c) =>
        !char.IsControl(c) && c is not ('/' or '\\' or ':' or '*' or '?' or '"' or '<' or '>' or '|');
}
