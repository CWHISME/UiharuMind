/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Files;

// 用接口而不用抽象基类:只有一个实现,没有可复用的算法骨架,继承只会加耦合。
// 调用方用组合持有(构造时传入),而不是静态入口:测试可注入临时目录,不污染真实备份区。
/// <summary>
/// 覆盖写之前的自动备份。备份是源文件的逐字节拷贝,落在备份根下按源文件分桶的目录里,
/// 只保留最近若干份。备份失败永不抛异常——调用方照常写,只是本次没有备份可报。
/// 做成公共接口:Write 工具之外,其它覆盖写用户文件的模块也用得上。
/// </summary>
public interface IFileBackupStore
{
    /// <summary>备份根目录(绝对路径)</summary>
    string BackupRoot { get; }

    /// <summary>
    /// 备份一份文件内容
    /// </summary>
    /// <param name="sourcePath">源文件绝对路径(只用于分桶命名,不读盘)</param>
    /// <param name="content">源文件字节(调用方读盘时顺手带来,避免二次读盘)</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>备份绝对路径;失败返回 null</returns>
    Task<string?> BackupAsync(string sourcePath, byte[] content, CancellationToken ct = default);
}

/// <summary>默认实现,见 <see cref="IFileBackupStore"/>。</summary>
public sealed class FileBackupStore : IFileBackupStore
{
    private readonly int _keptPerFile; //每源文件保留份数

    /// <summary>默认每源文件保留份数</summary>
    public const int DefaultKeptPerFile = 10;

    /// <inheritdoc />
    public string BackupRoot { get; }

    /// <summary>
    /// 构造备份存储
    /// </summary>
    /// <param name="backupRoot">备份根目录;空则用全局缓存 <c>AppPaths.Cache.FileBackups</c></param>
    /// <param name="keptPerFile">每源文件保留份数</param>
    public FileBackupStore(string? backupRoot = null, int keptPerFile = DefaultKeptPerFile)
    {
        BackupRoot = backupRoot ?? AppPaths.Cache.FileBackups;
        _keptPerFile = Math.Max(1, keptPerFile);
    }

    /// <inheritdoc />
    public async Task<string?> BackupAsync(string sourcePath, byte[] content, CancellationToken ct = default)
    {
        try
        {
            string bucket = Path.Combine(BackupRoot, BucketName(sourcePath));
            Directory.CreateDirectory(bucket);
            string backupPath = Path.Combine(bucket, BackupFileName());
            await File.WriteAllBytesAsync(backupPath, content, ct).ConfigureAwait(false);
            PruneBucket(bucket);
            return backupPath;
        }
        catch
        {
            return null; //备份是保险,失败不阻断写入
        }
    }

    // 分桶:原文件名(人类可认) + 源路径 hash 后缀。名字在前,浏览时按文件名排序;
    // hash 防同名文件互串。路径先转小写再 hash:与文件锁表的 OrdinalIgnoreCase 口径一致,
    // 大小写不敏感系统上同文件同桶
    private static string BucketName(string sourcePath)
    {
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourcePath.ToLowerInvariant())))[..16];
        string baseName = Path.GetFileName(sourcePath);
        foreach (char invalid in Path.GetInvalidFileNameChars()) baseName = baseName.Replace(invalid, '_');
        if (string.IsNullOrWhiteSpace(baseName)) baseName = "file";
        return $"{baseName}_{hash}";
    }

    // 文件名按时间可排序,并发同毫秒写加 guid 短缀防撞
    private static string BackupFileName()
    {
        string suffix = Guid.NewGuid().ToString("N")[..8];
        return $"{DateTime.UtcNow:yyyyMMdd-HHmmss-ffffff}_{suffix}.bak";
    }

    // 只留最近份:文件名时间可排序,排完删尾。最佳努力,删失败不管
    private void PruneBucket(string bucket)
    {
        try
        {
            string[] files = Directory.GetFiles(bucket, "*.bak");
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i + _keptPerFile < files.Length; i++)
            {
                try
                {
                    File.Delete(files[i]);
                }
                catch
                {
                    // 单份清理失败不影响其余
                }
            }
        }
        catch
        {
            // 清理失败不影响备份结论
        }
    }
}
