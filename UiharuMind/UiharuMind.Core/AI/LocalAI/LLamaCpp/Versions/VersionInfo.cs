using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.LLamaCpp.Versions;

public class VersionInfo : IComparable<VersionInfo>, IDownloadable, IEquatable<VersionInfo>
{
    private bool _isDownloaded;

    public string Name => VersionNumber;
    public string VersionNumber { get; }
    public List<IBackendType> BackendTypes { get; private set; }

    public string ExecutablePath { get; set; }
    public string DownloadUrl { get; set; }

    public string? DownloadFileName { get; set; }

    public string? DownloadDirectory { get; set; }

    // public string? DownloadSize { get; set; }
    public bool IsDownloaded
    {
        get => _isDownloaded;
        set
        {
            _isDownloaded = value;
            if (value) DownloadDirectory = ExecutablePath;
        }
    }

    public VersionInfo(string versionNumber)
    {
        VersionNumber = versionNumber;
        BackendTypes = new List<IBackendType>();
    }

    public void AddBackendType(IBackendType backendType)
    {
        BackendTypes.Add(backendType);
    }

    public void Preview()
    {
        Console.WriteLine($"Previewing Version: {VersionNumber}");
        foreach (var backendType in BackendTypes)
        {
            Console.WriteLine($"- {backendType.Name}");
        }
    }

    public void Update()
    {
        Console.WriteLine($"Updating Version: {VersionNumber}");
        // 更新逻辑
    }

    public int CompareTo(VersionInfo? other)
    {
        if (other == null) return 1;

        if (this.IsDownloaded && !other.IsDownloaded)
        {
            return -1; // this 在前
        }

        if (!this.IsDownloaded && other.IsDownloaded)
        {
            return 1; // other 在前
        }

        return String.Compare(other.VersionNumber, this.VersionNumber, StringComparison.Ordinal);
    }

    public bool Equals(VersionInfo? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return VersionNumber == other.VersionNumber;
    }

    public override bool Equals(object? obj)
    {
        if (obj is null) return false;
        if (ReferenceEquals(this, obj)) return true;
        if (obj.GetType() != GetType()) return false;
        return Equals((VersionInfo)obj);
    }

    public override int GetHashCode()
    {
        return VersionNumber.GetHashCode();
    }
}