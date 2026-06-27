using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Utils;

public static class SimpleArchiveHelper
{
    private static readonly string[] SupportedArchiveSuffixes =
    [
        ".zip", ".tar", ".tar.gz", ".tgz", ".tar.xz", ".txz",
        ".tar.bz2", ".tbz2", ".7z", ".rar", ".gz", ".xz", ".bz2", ".zst", ".zstd"
    ];

    public static bool IsSupportedArchiveFileName(string fileName)
    {
        return SupportedArchiveSuffixes.Any(suffix =>
            fileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task ExtractArchiveAsync(
        string archivePath,
        string extractPath,
        bool deleteArchive = false,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(archivePath))
        {
            throw new FileNotFoundException("Archive file does not exist.", archivePath);
        }

        Directory.CreateDirectory(extractPath);
        string extractRoot = Path.GetFullPath(extractPath);

        await Task.Run(() =>
        {
            using IArchive archive = ArchiveFactory.OpenArchive(archivePath, new ReaderOptions());
            foreach (IArchiveEntry entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string entryKey = entry.Key ?? string.Empty;
                string destinationPath = GetSafeDestinationPath(extractRoot, entryKey);
                if (entry.IsDirectory)
                {
                    Directory.CreateDirectory(destinationPath);
                    if (!IsSamePath(destinationPath, extractRoot))
                    {
                        TryApplyUnixMode(entry, destinationPath);
                    }

                    continue;
                }

                // SharpCompress 负责处理 zip/tar/7z 等格式；写入前先做路径穿越检查。
                entry.WriteToDirectory(extractRoot, new ExtractionOptions
                {
                    ExtractFullPath = true,
                    Overwrite = true,
                    PreserveFileTime = true,
                    SymbolicLinkHandler = (linkPath, linkTarget) =>
                        CreateSymbolicLinkSafely(extractRoot, linkPath, linkTarget)
                });
                TryApplyUnixMode(entry, destinationPath);
            }
        }, cancellationToken).ConfigureAwait(false);

        if (deleteArchive)
        {
            File.Delete(archivePath);
            Log.Debug($"Archive file deleted: {archivePath}");
        }
    }

    private static void CreateSymbolicLinkSafely(string extractRoot, string linkPath, string linkTarget)
    {
        if (string.IsNullOrWhiteSpace(linkTarget))
        {
            throw new InvalidDataException($"Archive contains an empty symbolic link target: {linkPath}");
        }

        string fullLinkPath = Path.GetFullPath(linkPath);
        EnsurePathIsInsideRoot(extractRoot, fullLinkPath, linkPath);

        string fullTargetPath = Path.IsPathFullyQualified(linkTarget)
            ? Path.GetFullPath(linkTarget)
            : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullLinkPath) ?? extractRoot, linkTarget));
        EnsurePathIsInsideRoot(extractRoot, fullTargetPath, linkTarget);

        string? parent = Path.GetDirectoryName(fullLinkPath);
        if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);

        // 软链接本身也可能来自下载包，创建前同样走根目录约束，避免解压时写出目标目录。
        if (File.Exists(fullLinkPath) || Directory.Exists(fullLinkPath))
        {
            File.Delete(fullLinkPath);
        }

        File.CreateSymbolicLink(fullLinkPath, linkTarget);
    }

    private static string GetSafeDestinationPath(string extractRoot, string entryKey)
    {
        if (string.IsNullOrWhiteSpace(entryKey))
        {
            throw new InvalidDataException("Archive contains an empty entry name.");
        }

        string destinationPath = Path.GetFullPath(Path.Combine(
            extractRoot,
            entryKey
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar)));

        EnsurePathIsInsideRoot(extractRoot, destinationPath, entryKey);
        return destinationPath;
    }

    private static void EnsurePathIsInsideRoot(string extractRoot, string path, string sourceName)
    {
        string normalizedPath = Path.GetFullPath(path);
        string rootWithoutSeparator = extractRoot.TrimEnd(Path.DirectorySeparatorChar);
        string normalizedRoot = extractRoot.EndsWith(Path.DirectorySeparatorChar)
            ? extractRoot
            : extractRoot + Path.DirectorySeparatorChar;

        if (!string.Equals(normalizedPath, rootWithoutSeparator, StringComparison.Ordinal)
            && !normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Archive entry escapes target directory: {sourceName}");
        }
    }

    private static void TryApplyUnixMode(IArchiveEntry entry, string destinationPath)
    {
        if (OperatingSystem.IsWindows()
            || !File.Exists(destinationPath) && !Directory.Exists(destinationPath)
            || File.GetAttributes(destinationPath).HasFlag(FileAttributes.ReparsePoint))
        {
            return;
        }

        long? mode = TryGetEntryMode(entry);
        if (!mode.HasValue) return;

        var unixMode = (UnixFileMode)(mode.Value & 0xFFF);
        if (unixMode == 0) return;

        File.SetUnixFileMode(destinationPath, unixMode);
    }

    private static long? TryGetEntryMode(IArchiveEntry entry)
    {
        object? value;
        try
        {
            value = entry.GetType().GetProperty("Mode")?.GetValue(entry);
        }
        catch
        {
            return null;
        }

        return value switch
        {
            long longValue => longValue,
            int intValue => intValue,
            short shortValue => shortValue,
            _ => null
        };
    }

    private static bool IsSamePath(string left, string right)
    {
        return string.Equals(
            Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.Ordinal);
    }
}
