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

using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Versions;
using System.Text.RegularExpressions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppVersionManager
{
    private readonly VersionManager _versionManager = new VersionManager();

    /// <summary>
    /// 获取目录中本地引擎版本信息
    /// </summary>
    /// <param name="path"></param>
    /// <param name="forceNew"></param>
    /// <returns></returns>
    public async Task<VersionManager> GetLocalVersions(string path, bool forceNew = false)
    {
        VersionManager versionManager = forceNew ? new VersionManager() : _versionManager;
        await Task.Run(() =>
        {
            foreach (var item in versionManager.VersionsList)
            {
                item.IsDownloaded = false;
            }

            foreach (string versionDir in Directory.EnumerateDirectories(path))
            {
                string versionName = Path.GetFileName(versionDir);
                var version = versionManager.GetOrCreateVersion(versionName);
                version.AddBackendType(new LLamaCppRuntimeEngine(versionName, versionDir));
                //判断 versionDir 下是否有可执行文件
                foreach (var file in Directory.EnumerateFiles(versionDir, LLamaCppSettingConfig.ServerExeName + "*",
                             SearchOption.AllDirectories))
                {
                    version.ExecutablePath = Path.GetDirectoryName(file)!;
                    break;
                }

                if (!string.IsNullOrEmpty(version.ExecutablePath)) version.IsDownloaded = true;
            }

            versionManager.RemoveAllNotLoadedVersions();
            versionManager.Sort();
        }).ConfigureAwait(false);

        return versionManager;
    }

    /// <summary>
    /// 拉取最新版本列表(注：会包含本地已有的版本)
    /// </summary>
    /// <returns></returns>
    public async Task<VersionManager> GetLatestVersion(string path)
    {
        GitHubReleaseInfo? release = await GitHubReleaseAssetHelper
            .GetLatestReleaseAsync("ggerganov", "llama.cpp")
            .ConfigureAwait(false);
        if (release == null) return _versionManager;

        string? releaseDate = release.PublishedAt.HasValue
            ? TimeUtils.TimeStringToLocalTimeString(release.PublishedAt.Value.ToString("O"))
            : null;
        _versionManager.ReleaseDate = $"Release Date: {releaseDate} \n\n{release.Body}";

        IReadOnlyList<GitHubReleaseAssetInfo> assets = GitHubReleaseAssetHelper.SelectPlatformAssets(
            release.Assets,
            new GitHubReleaseAssetSelectOptions(NamePrefix: "llama-"));
        foreach (GitHubReleaseAssetInfo asset in assets)
        {
            string name = asset.Name;
            var version = _versionManager.GetOrCreateVersion(Path.GetFileNameWithoutExtension(name));
            if (version.IsDownloaded) continue;
            version.DownloadUrl = asset.DownloadUrl;
            version.DownloadFileName = ReleaseVersionPackageManager.GetPackageFilePath(path, asset.Name);
            version.ExecutablePath = ReleaseVersionPackageManager.GetInstallDirectoryFromVersion(path, version.VersionNumber);
        }

        _versionManager.Sort();
        return _versionManager;
    }
}
