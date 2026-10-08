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

using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// llama.cpp（ggml-org/llama.cpp）的版本管理，以 llama-server 存在为安装成功的判据。
/// 随应用附带的引擎（若有）也一并列出，但不许删
/// </summary>
/// <param name="preferStable">走正式通道：跟正式版钉住的构建，而不是最近一次预览构建</param>
/// <param name="bundledRoot">随应用附带的引擎目录；没有为 null</param>
public class LLamaCppVersionManager(Func<bool> preferStable, string? bundledRoot = null)
    : EngineVersionManager<VersionInfo>
{
    public override string ServerExeName => LLamaCppSettingConfig.ServerExeName;

    protected override string Owner => "ggml-org";
    protected override string Repository => "llama.cpp";

    // llama.cpp 的 b 系列构建都标为预发布，正式版不带包：预览通道走最近可用，正式通道走正式版钉住的构建
    protected override bool IncludePrereleases => true;

    public override Task<IReadOnlyList<VersionInfo>> ScanLocalAsync(string rootDirectory,
        CancellationToken cancellationToken = default)
    {
        return ExclusiveAsync(async () =>
        {
            await ScanLocalVersionsAsync(rootDirectory, Versions, Versions.Count > 0, null, cancellationToken)
                .ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(bundledRoot) && Directory.Exists(bundledRoot))
                await ScanLocalVersionsAsync(bundledRoot, Versions, false, version => version.IsNotAllowDelete = true,
                    cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    protected override async Task<GitHubReleaseInfo?> ResolveRemoteReleaseAsync(
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken)
    {
        if (!preferStable())
            return await base.ResolveRemoteReleaseAsync(accept, cancellationToken).ConfigureAwait(false);
        GitHubReleaseInfo? pinned = await GitHubReleaseAssetHelper
            .GetStablePinnedReleaseAsync(Owner, Repository, cancellationToken).ConfigureAwait(false);
        return pinned != null && accept(pinned) ? pinned : null;
    }

    protected override GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions(NamePrefix: "llama-");
    }
}
