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
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Runtime.Backends;

public class LLamaCppVersionManager : ReleaseVersionManagerBase<VersionInfo>
{
    private readonly VersionManager _versionManager = new VersionManager();

    protected override string Owner => "ggml-org";
    protected override string Repository => "llama.cpp";

    // llama.cpp 的 b 系列构建都标为预发布，正式版不带包：预览通道走最近可用，正式通道走正式版钉住的构建
    protected override bool IncludePrereleases => true;

    /// <summary>
    /// 正式版通道：跟随最新正式版钉住的构建（如 v0.6.0 → b11429），更新频率更低；默认关闭，保持预览版行为
    /// </summary>
    public bool PreferStableReleases { get; set; }

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

    protected override async Task<GitHubReleaseInfo?> ResolveRemoteReleaseAsync(
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken)
    {
        if (!PreferStableReleases)
            return await base.ResolveRemoteReleaseAsync(accept, cancellationToken).ConfigureAwait(false);
        GitHubReleaseInfo? pinned = await GitHubReleaseAssetHelper
            .GetStablePinnedReleaseAsync(Owner, Repository, cancellationToken).ConfigureAwait(false);
        return pinned != null && accept(pinned) ? pinned : null;
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
        // 发布说明夹着 HTML（<details> 等），整理成 markdown 交给界面渲染
        string body = MarkdownCleaner.Clean(release.Body ?? "");
        string title = releaseDate == null ? $"**{release.TagName}**" : $"**{release.TagName}** · {releaseDate}";
        return $"{title}\n\n{body}".TrimEnd();
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
