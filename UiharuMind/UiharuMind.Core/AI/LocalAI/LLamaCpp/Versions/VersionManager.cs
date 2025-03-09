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

namespace UiharuMind.Core.LLamaCpp.Versions;

public class VersionManager
{
    public string? ReleaseDate { get; set; }
    public readonly List<VersionInfo> VersionsList = new List<VersionInfo>();

    public void AddVersion(VersionInfo versionInfo, bool withSort = true)
    {
        if (!VersionsList.Contains(versionInfo))
        {
            VersionsList.Add(versionInfo);
            if (withSort) Sort();
        }
    }

    public void RemoveVersion(string versionNumber)
    {
        VersionsList.RemoveAll(x => x.VersionNumber == versionNumber);
    }

    public VersionInfo? GetVersion(string versionNumber)
    {
        return VersionsList.FirstOrDefault(x => x.VersionNumber == versionNumber);
    }

    public VersionInfo GetOrCreateVersion(string versionNumber)
    {
        var versionInfo = VersionsList.FirstOrDefault(x => x.VersionNumber == versionNumber);
        if (versionInfo == null)
        {
            versionInfo = new VersionInfo(versionNumber);
            AddVersion(versionInfo);
        }

        return versionInfo;
    }

    /// <summary>
    /// 删除所有未下载的版本
    /// </summary>
    public void RemoveAllNotLoadedVersions()
    {
        VersionsList.RemoveAll(x => !x.IsDownloaded);
    }

    public void Sort()
    {
        VersionsList.Sort();
    }

    public void RemoveAllVersions()
    {
        VersionsList.Clear();
    }

    public void Merge(VersionManager otherVersion)
    {
        foreach (var other in otherVersion.VersionsList)
        {
            AddVersion(other, false);
        }

        // VersionsList.AddRange(internalVersion.VersionsList);
        Sort();
    }
}