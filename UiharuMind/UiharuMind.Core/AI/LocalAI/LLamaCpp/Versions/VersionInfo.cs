namespace UiharuMind.Core.LLamaCpp.Versions;

public class VersionInfo : IComparable<VersionInfo>
{
    public string VersionNumber { get; private set; }
    public List<IBackendType> BackendTypes { get; private set; }

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
        return String.Compare(other?.VersionNumber, VersionNumber, StringComparison.Ordinal);
    }
}