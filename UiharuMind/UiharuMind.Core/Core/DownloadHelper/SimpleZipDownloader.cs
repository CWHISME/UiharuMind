using System.IO.Compression;

namespace UiharuMind.Core.Core.Utils;

public class SimpleZipDownloader
{
    public event Action<long, long>? DownloadProgressChanged;
    public event Action<string>? ExtractProgressChanged;

    public async Task DownloadAndExtractAsync(string url, string extractPath)
    {
        // 定义缓存目录，用于存储下载的 ZIP 文件
        string cacheDirectory = Path.Combine(Path.GetTempPath(), "ZipDownloaderCache");
        if (!Directory.Exists(cacheDirectory))
        {
            Directory.CreateDirectory(cacheDirectory);
        }

        // 确定下载路径，文件名取自 URL
        string downloadPath = Path.Combine(cacheDirectory, Path.GetFileName(url));

        try
        {
            // 下载 ZIP 文件
            await DownloadFileAsync(url, downloadPath);

            // 解压 ZIP 文件
            ExtractZipFile(downloadPath, extractPath);

            // 删除缓存 ZIP 文件
            DeleteFile(downloadPath);

            Console.WriteLine("文件下载、解压并删除原文件完成。");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"发生错误: {ex.Message}");
        }
    }

    private async Task DownloadFileAsync(string url, string downloadPath)
    {
        long existingFileSize = 0;
        string eTag = null;

        // 检查是否有已存在的部分文件，并读取其大小和 ETag
        if (File.Exists(downloadPath))
        {
            existingFileSize = new FileInfo(downloadPath).Length;

            // 将 ETag 存储在一个伴随文件中，如 "downloadPath.etag"
            string eTagPath = downloadPath + ".etag";
            if (File.Exists(eTagPath))
            {
                eTag = await File.ReadAllTextAsync(eTagPath);
            }
        }

        using (HttpClient client = new HttpClient())
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existingFileSize > 0)
            {
                // 设置 Range 头以请求从文件的最后位置继续下载
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingFileSize, null);

                // 如果有 ETag，可以添加 If-Range 头部
                if (!string.IsNullOrEmpty(eTag))
                {
                    request.Headers.IfRange =
                        new System.Net.Http.Headers.RangeConditionHeaderValue(
                            new System.Net.Http.Headers.EntityTagHeaderValue(eTag));
                }
            }

            try
            {
                HttpResponseMessage response =
                    await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                // 检查 ETag 或 Last-Modified 头部
                if (response.Headers.ETag != null)
                {
                    eTag = response.Headers.ETag.Tag;
                    // 保存新的 ETag 到文件
                    await File.WriteAllTextAsync(downloadPath + ".etag", eTag);
                }

                long? totalBytes = response.Content.Headers.ContentRange?.Length ??
                                   response.Content.Headers.ContentLength;
                using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                       fileStream = new FileStream(downloadPath, FileMode.Append, FileAccess.Write, FileShare.Read,
                           8192, true))
                {
                    var buffer = new byte[8192];
                    long totalRead = existingFileSize;
                    int bytesRead;
                    while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) != 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        totalRead += bytesRead;

                        // 更新进度
                        DownloadProgressChanged?.Invoke(totalRead, totalBytes ?? -1);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"下载失败: {ex.Message}");
            }
        }
    }

    private void ExtractZipFile(string zipFilePath, string extractPath)
    {
        if (!Directory.Exists(extractPath))
        {
            Directory.CreateDirectory(extractPath);
        }

        try
        {
            using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
            {
                long totalEntries = archive.Entries.Count;
                long currentEntry = 0;

                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string destinationPath = Path.Combine(extractPath, entry.FullName);
                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        // 是目录
                        Directory.CreateDirectory(destinationPath);
                    }
                    else
                    {
                        // 是文件
                        entry.ExtractToFile(destinationPath, overwrite: true);
                    }

                    currentEntry++;
                    ExtractProgressChanged?.Invoke(
                        $"{currentEntry}/{totalEntries} ({(double)currentEntry / totalEntries:P})");
                }
            }
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"解压过程中发生 IO 错误: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Console.WriteLine($"解压过程中发生权限错误: {uaEx.Message}");
        }
    }

    private void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Console.WriteLine("原 ZIP 文件已删除。");
            }
        }
        catch (IOException ioEx)
        {
            Console.WriteLine($"删除文件过程中发生 IO 错误: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Console.WriteLine($"删除文件过程中发生权限错误: {uaEx.Message}");
        }
    }
}