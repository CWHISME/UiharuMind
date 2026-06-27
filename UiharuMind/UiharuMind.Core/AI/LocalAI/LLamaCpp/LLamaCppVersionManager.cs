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

using System.Text.RegularExpressions;
using UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Core.LLamaCpp.Versions;

namespace UiharuMind.Core.LLamaCpp;

public class LLamaCppVersionManager : ReleaseVersionManagerBase<VersionInfo>
{
    private readonly VersionManager _versionManager = new VersionManager();

    protected override string Owner => "ggerganov";
    protected override string Repository => "llama.cpp";

    /// <summary>
    /// 获取目录中本地引擎版本信息
    /// </summary>
    public async Task<VersionManager> GetLocalVersions(string path, bool forceNew = false)
    {
        await GetLocalVersionsAsync(path, forceNew).ConfigureAwait(false);
        return SyncVersionManager();
    }

    public async Task<VersionManager> GetLocalVersions(
        string path,
        string? internalPath)
    {
        await GetLocalVersionsAsync(path).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(internalPath) && Directory.Exists(internalPath))
        {
            await ScanLocalVersionsAsync(
                    internalPath,
                    Versions,
                    false,
                    version => version.IsNotAllowDelete = true)
                .ConfigureAwait(false);
        }

        SortVersions();
        return SyncVersionManager();
    }

    /// <summary>
    /// 拉取最新版本列表(注：会包含本地已有的版本)
    /// </summary>
    public async Task<VersionManager> GetLatestVersion(string path)
    {
        await PullLatestVersionsAsync(path).ConfigureAwait(false);
        return SyncVersionManager();
    }

    protected override void ConfigureLocalVersion(VersionInfo version, string versionDirectory)
    {
        version.ExecutablePath = versionDirectory;
    }

    protected override GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions(NamePrefix: "llama-");
    }

    protected override ManagedVersionValidationResult ValidateInstalledVersion(VersionInfo version)
    {
        if (!Directory.Exists(version.InstallDirectory))
        {
            return new ManagedVersionValidationResult(false, "Runtime directory does not exist.");
        }

        foreach (string file in Directory.EnumerateFiles(
                     version.InstallDirectory,
                     LLamaCppSettingConfig.ServerExeName + "*",
                     SearchOption.AllDirectories))
        {
            // llama.cpp 的可用性仍以找到 llama-server 可执行文件为准。
            version.ExecutablePath = Path.GetDirectoryName(file)!;
            return ManagedVersionValidationResult.Valid;
        }

        return new ManagedVersionValidationResult(false, $"{LLamaCppSettingConfig.ServerExeName} was not found.");
    }

    protected override string CreateReleaseInfoText(GitHubReleaseInfo release)
    {
        string? releaseDate = release.PublishedAt.HasValue
            ? TimeUtils.TimeStringToLocalTimeString(release.PublishedAt.Value.ToString("O"))
            : null;
        return $"Release Date: {releaseDate} \n\n{release.Body}";
    }

    private VersionManager SyncVersionManager()
    {
        _versionManager.RemoveAllVersions();
        _versionManager.ReleaseDate = ReleaseInfoText;
        foreach (VersionInfo version in VersionsList)
        {
            _versionManager.AddVersion(version, false);
        }

        _versionManager.Sort();
        return _versionManager;
    }
}
