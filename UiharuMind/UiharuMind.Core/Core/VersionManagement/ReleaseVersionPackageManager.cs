namespace UiharuMind.Core.Core.Utils;

public static class ReleaseVersionPackageManager
{
    public static string GetPackageFilePath(string rootDirectory, string packageName)
    {
        return Path.Combine(rootDirectory, ToSafeFileName(packageName));
    }

    public static string GetInstallDirectoryFromPackageName(string rootDirectory, string packageName)
    {
        return Path.Combine(rootDirectory, Path.GetFileNameWithoutExtension(ToSafeFileName(packageName)));
    }

    public static string GetInstallDirectoryFromVersion(string rootDirectory, string versionName)
    {
        return Path.Combine(rootDirectory, ToSafeFileName(versionName));
    }

    public static bool IsInstalledDirectory(string? path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               Directory.EnumerateFileSystemEntries(path).Any();
    }

    public static string ToSafeFileName(string value)
    {
        return string.Join("_", value.Split(Path.GetInvalidFileNameChars()));
    }
}
