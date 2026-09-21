/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.IO.Compression;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public static class SimpleZipHelper
{
    public static async Task ExtractZipFile(string zipFilePath, string extractPath, bool isDeleteFile = false)
    {
        string fileName = Path.GetFileName(zipFilePath);
        if (!Directory.Exists(extractPath))
        {
            Directory.CreateDirectory(extractPath);
        }

        try
        {
            await Task.Run(() =>
            {
                using (ZipArchive archive = ZipFile.OpenRead(zipFilePath))
                {
                    long totalEntries = archive.Entries.Count;
                    long currentEntry = 0;

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        string destinationPath = Path.Combine(extractPath, entry.FullName);

                        // 确保父目录存在
                        string? directoryPath = Path.GetDirectoryName(destinationPath);
                        if (!string.IsNullOrEmpty(directoryPath) && !Directory.Exists(directoryPath))
                        {
                            Directory.CreateDirectory(directoryPath);
                        }

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
                        Log.Debug(
                            $"{fileName} 解压中... {currentEntry}/{totalEntries} ({(double)currentEntry / totalEntries:P})");
                    }
                }

                if (isDeleteFile) DeleteFile(zipFilePath);
            }).ConfigureAwait(false);
        }
        catch (IOException ioEx)
        {
            Log.Error($"解压过程中发生 IO 错误: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Log.Error($"解压过程中发生权限错误: {uaEx.Message}");
        }
        catch (Exception ex)
        {
            Log.Error($"解压过程中发生未知错误: {ex.Message}");
        }
    }

    public static void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Log.Debug("原 ZIP 文件已删除。");
            }
        }
        catch (IOException ioEx)
        {
            Log.Error($"删除文件过程中发生 IO 错误: {ioEx.Message}");
        }
        catch (UnauthorizedAccessException uaEx)
        {
            Log.Error($"删除文件过程中发生权限错误: {uaEx.Message}");
        }
    }
}