namespace UiharuMind.Core.Core.Utils;

public class ManagedVersionPackage : IDownloadable, IInstalledDownloadable, IComparable<ManagedVersionPackage>,
    IEquatable<ManagedVersionPackage>
{
    private bool _isDownloaded;

    public string Name { get; set; } = string.Empty;
    public virtual string DownloadUrl { get; set; } = string.Empty;
    public string PackageFilePath { get; set; } = string.Empty;
    public string InstallDirectory { get; set; } = string.Empty;
    public long AssetSize { get; set; }
    public Version Version { get; set; } = new(0, 0);
    public string? ReleaseUrl { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public string? ReleaseBody { get; set; }
    public string? ValidationError { get; set; }
    public bool IsInstalled { get; set; }
    public virtual bool IsNotAllowDelete { get; set; }
    public virtual string? DownloadFileName
    {
        get => PackageFilePath;
        set => PackageFilePath = value ?? string.Empty;
    }
    public virtual string? DownloadDirectory => null;
    public virtual string? InstalledPath => InstallDirectory;

    public virtual bool IsDownloaded
    {
        get => _isDownloaded;
        set => _isDownloaded = value;
    }

    public void RefreshState()
    {
        bool installPathExists = !string.IsNullOrWhiteSpace(InstallDirectory) &&
                                 (File.Exists(InstallDirectory) || Directory.Exists(InstallDirectory));
        if (!installPathExists)
        {
            IsInstalled = false;
        }

        IsDownloaded = File.Exists(PackageFilePath) || IsInstalled;
    }

    public virtual int CompareTo(ManagedVersionPackage? other)
    {
        if (other == null) return 1;

        if (IsDownloaded && !other.IsDownloaded) return -1;
        if (!IsDownloaded && other.IsDownloaded) return 1;

        int versionCompare = other.Version.CompareTo(Version);
        if (versionCompare != 0) return versionCompare;

        return string.Compare(other.Name, Name, StringComparison.Ordinal);
    }

    public bool Equals(ManagedVersionPackage? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Name, other.Name, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is ManagedVersionPackage other && Equals(other);
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode(StringComparison.Ordinal);
    }

    public static Version ParseVersion(string rawVersion)
    {
        string version = rawVersion.Trim().TrimStart('v', 'V');
        var numericVersion = System.Text.RegularExpressions.Regex.Match(version, @"\d+(\.\d+){0,3}");
        if (numericVersion.Success) version = numericVersion.Value;
        return Version.TryParse(version, out Version? parsed) ? parsed : new Version(0, 0);
    }

    public static bool IsRemoteVersionNewer(Version remote, Version local)
    {
        return NormalizeVersionPartCount(remote) > NormalizeVersionPartCount(local);
    }

    private static Version NormalizeVersionPartCount(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }
}

public sealed record ManagedVersionValidationResult(bool IsValid, string? Error = null)
{
    public static ManagedVersionValidationResult Valid { get; } = new(true);
}
