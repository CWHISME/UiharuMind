using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// stable-diffusion.cpp（leejet/stable-diffusion.cpp）的版本管理，以 sd-server 存在为安装成功的判据。
/// 上游每次推送都发一版、命令行参数常改，所以认一个验证过的发布（<see cref="PinnedTag"/>），最新的那版也列出来可以手动装
/// </summary>
public class SdCppVersionManager : EngineVersionManager<SdCppVersion>
{
    /// <summary>
    /// 服务端可执行文件名（不含扩展名）
    /// </summary>
    public const string ExecutableName = "sd-server";

    /// <summary>
    /// 验证过的发布：更新提示与自动安装只到这一版，随应用发版往前挪
    /// </summary>
    public const string PinnedTag = "master-945-a1ded76";

    /// <summary>
    /// 验证过的发布号
    /// </summary>
    public static Version PinnedVersion { get; } = ManagedVersionPackage.ParseVersion(PinnedTag);

    public override string ServerExeName => ExecutableName;

    protected override string Owner => "leejet";
    protected override string Repository => "stable-diffusion.cpp";

    protected override GitHubReleaseAssetSelectOptions GetAssetSelectOptions()
    {
        return new GitHubReleaseAssetSelectOptions(NamePrefix: "sd-");
    }

    protected override bool ShouldIncludeAsset(GitHubReleaseAssetInfo asset)
    {
        return !asset.Name.Contains("-cuda", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.Contains("rocm", StringComparison.OrdinalIgnoreCase)
               && !asset.Name.Contains("cudart", StringComparison.OrdinalIgnoreCase);
    }

    // 包名不带构建号（sd-master-<sha>-bin-…），版本名换成 tag 打头（master-945-<sha>-bin-…），版本才比得出先后
    protected override string GetRemoteVersionName(GitHubReleaseInfo release, GitHubReleaseAssetInfo asset)
    {
        string name = SimpleArchiveHelper.GetNameWithoutArchiveExtension(asset.Name);
        int bin = name.IndexOf("-bin-", StringComparison.Ordinal);
        return bin < 0 ? name : release.TagName + name[bin..];
    }

    // 验证过的那版在前（发布说明取它的），最新的一版在后
    protected override async Task<IReadOnlyList<GitHubReleaseInfo>> ResolveRemoteReleasesAsync(
        Func<GitHubReleaseInfo, bool> accept,
        CancellationToken cancellationToken)
    {
        GitHubReleaseInfo? pinned = await GitHubReleaseAssetHelper
            .GetReleaseByTagAsync(Owner, Repository, PinnedTag, cancellationToken).ConfigureAwait(false);
        GitHubReleaseInfo? latest = await GitHubReleaseAssetHelper
            .GetLatestReleaseAsync(Owner, Repository, cancellationToken).ConfigureAwait(false);
        List<GitHubReleaseInfo> releases = [];
        if (pinned != null && accept(pinned)) releases.Add(pinned);
        if (latest != null && latest.TagName != PinnedTag && accept(latest)) releases.Add(latest);
        return releases;
    }
}
