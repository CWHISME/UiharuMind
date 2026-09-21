using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public static class SimpleDownloadHelper
{
    public static async Task<byte[]?> DownloadFileAsync(string url)
    {
        try
        {
            // 使用 HttpClient 下载图像数据
            using (var httpClient = new HttpClient())
            {
                byte[] imageBytes = await httpClient.GetByteArrayAsync(url);
                return imageBytes;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"Error downloading image: {ex.Message}");
            return null;
        }
    }
}