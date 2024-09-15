namespace UiharuMind.Core.LLamaCpp.Versions;

public class VersionManager
{
    private List<VersionInfo> _versionsList = new List<VersionInfo>();

    public void AddVersion(VersionInfo versionInfo, bool withSort = true)
    {
        _versionsList.Add(versionInfo);
        if (withSort) Sort();
    }

    public void RemoveVersion(string versionNumber)
    {
        _versionsList.RemoveAll(x => x.VersionNumber == versionNumber);
    }

    public VersionInfo? GetVersion(string versionNumber)
    {
        return _versionsList.FirstOrDefault(x => x.VersionNumber == versionNumber);
    }

    public VersionInfo GetOrCreateVersion(string versionNumber)
    {
        var versionInfo = _versionsList.FirstOrDefault(x => x.VersionNumber == versionNumber);
        if (versionInfo == null)
        {
            versionInfo = new VersionInfo(versionNumber);
            AddVersion(versionInfo);
        }

        return versionInfo;
    }

    public void Sort()
    {
        _versionsList.Sort();
    }
}