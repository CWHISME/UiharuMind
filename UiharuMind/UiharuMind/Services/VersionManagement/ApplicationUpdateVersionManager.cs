using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Services;

internal sealed class ApplicationUpdateVersionManager : ReleaseVersionManagerBase<ManagedVersionPackage>
{
    protected override string Owner => "CWHISME";
    protected override string Repository => "UiharuMind";

    protected override bool ShouldIncludeAsset(GitHubReleaseAssetInfo asset)
    {
        return SimpleArchiveHelper.IsSupportedArchiveFileName(asset.Name);
    }

    protected override string GetRemoteVersionName(GitHubReleaseInfo release, GitHubReleaseAssetInfo asset)
    {
        return asset.Name;
    }

    protected override string GetRemoteInstallDirectory(
        string rootDirectory,
        string versionName,
        GitHubReleaseAssetInfo asset)
    {
        return GetInstallDirectoryFromPackageName(rootDirectory, asset.Name);
    }

    protected override bool ShouldKeepLocalVersion(ManagedVersionPackage version)
    {
        return false;
    }

    protected override ManagedVersionValidationResult ValidateInstalledVersion(ManagedVersionPackage version)
    {
        return ReleaseVersionPackageManager.IsInstalledDirectory(version.InstallDirectory)
            ? ManagedVersionValidationResult.Valid
            : new ManagedVersionValidationResult(false, "Application update package is empty or invalid.");
    }
}
