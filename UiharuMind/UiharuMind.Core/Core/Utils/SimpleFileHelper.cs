using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public static class SimpleFileHelper
{
    /// <summary>
    /// 异步获取指定目录的大小
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    public static async Task<long> GetFileOrDirectorySizeAsync(string? path)
    {
        return await Task.Run(() => GetFileOrDirectorySize(path));
    }

    /// <summary>
    /// 获取指定目录的大小
    /// </summary>
    /// <param name="path"></param>
    /// <returns></returns>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static long GetFileOrDirectorySize(string? path)
    {
        try
        {
            if (File.Exists(path))
            {
                // It's a file
                return new FileInfo(path).Length;
            }

            if (Directory.Exists(path))
            {
                // It's a directory
                return GetDirectorySize(path);
            }

            Log.Error($"The specified path {path} is neither a file nor a directory.");
            return -1;
        }
        catch (Exception ex)
        {
            Log.Error($"Error calculating size: {ex.Message}");
            return -1;
        }
    }

    private static long GetDirectorySize(string directoryPath)
    {
        long size = 0;

        //获取指定目录下所有文件大小
        foreach (var file in new DirectoryInfo(directoryPath).EnumerateFiles())
        {
            size += file.Length;
        }

        //递归获取所有子目录大小
        foreach (var subdir in new DirectoryInfo(directoryPath).EnumerateDirectories())
        {
            size += GetDirectorySize(subdir.FullName);
        }

        return size;
    }
}