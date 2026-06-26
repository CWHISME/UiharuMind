using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Services;

public partial class ApplicationUpdateService : ObservableObject
{
    private const string LatestReleasePageUrl =
        "https://github.com/CWHISME/UiharuMind/releases/latest";

    private readonly SemaphoreSlim _checkLock = new(1, 1);

    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private ApplicationUpdateRelease? _latestRelease;

    public bool HasAvailableUpdate =>
        LatestRelease != null && IsRemoteVersionNewer(LatestRelease.Version, App.Version);

    partial void OnLatestReleaseChanged(ApplicationUpdateRelease? value)
    {
        OnPropertyChanged(nameof(HasAvailableUpdate));
    }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!await _checkLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            IsCheckingForUpdates = true;
            LastError = null;
            LatestRelease = await FetchLatestReleaseAsync(cancellationToken).ConfigureAwait(false);
            HasChecked = true;
            OnPropertyChanged(nameof(HasAvailableUpdate));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.Warning(e.Message);
            LastError = e.Message;
            HasChecked = true;
        }
        finally
        {
            IsCheckingForUpdates = false;
            _checkLock.Release();
        }
    }

    private async Task<ApplicationUpdateRelease?> FetchLatestReleaseAsync(CancellationToken cancellationToken)
    {
        GitHubReleaseInfo? release = await GitHubReleaseAssetHelper
            .GetLatestReleaseAsync("CWHISME", "UiharuMind", cancellationToken)
            .ConfigureAwait(false);
        return BuildRelease(release);
    }

    private static ApplicationUpdateRelease? BuildRelease(GitHubReleaseInfo? release)
    {
        if (release == null || string.IsNullOrWhiteSpace(release.TagName)) return null;

        List<ApplicationUpdateAsset> assets = release.Assets
            .Select(asset => CreateAsset(release, asset))
            .ToList();
        GitHubReleaseAssetInfo? preferredAsset = GitHubReleaseAssetHelper.SelectPlatformAsset(
            release.Assets,
            new GitHubReleaseAssetSelectOptions(NameSuffix: ".zip"));

        return new ApplicationUpdateRelease(
            release.TagName,
            NormalizeVersion(release.TagName),
            release.Name,
            release.ReleaseUrl,
            release.PublishedAt,
            release.Body,
            assets,
            preferredAsset == null
                ? null
                : assets.FirstOrDefault(asset => asset.Name == preferredAsset.Name));
    }

    private static ApplicationUpdateAsset CreateAsset(
        GitHubReleaseInfo release,
        GitHubReleaseAssetInfo asset)
    {
        string directory = Path.Combine(SettingConfig.SaveDataPath, "ApplicationUpdates");
        Directory.CreateDirectory(directory);
        string packageFilePath = ReleaseVersionPackageManager.GetPackageFilePath(directory, asset.Name);
        string installDirectory = ReleaseVersionPackageManager.GetInstallDirectoryFromPackageName(directory, asset.Name);
        return new ApplicationUpdateAsset(
            release.TagName,
            asset.Name,
            asset.DownloadUrl,
            packageFilePath,
            installDirectory,
            asset.Size);
    }

    private static Version NormalizeVersion(string rawVersion)
    {
        string version = rawVersion.Trim().TrimStart('v', 'V');
        Match numericVersion = Regex.Match(version, @"\d+(\.\d+){0,3}");
        if (numericVersion.Success) version = numericVersion.Value;
        return Version.TryParse(version, out Version? parsed) ? parsed : new Version(0, 0);
    }

    private static bool IsRemoteVersionNewer(Version remote, Version local)
    {
        Version normalizedRemote = NormalizeVersionPartCount(remote);
        Version normalizedLocal = NormalizeVersionPartCount(local);
        return normalizedRemote > normalizedLocal;
    }

    private static Version NormalizeVersionPartCount(Version version)
    {
        return new Version(
            Math.Max(version.Major, 0),
            Math.Max(version.Minor, 0),
            Math.Max(version.Build, 0),
            Math.Max(version.Revision, 0));
    }

}

public sealed record ApplicationUpdateRelease(
    string TagName,
    Version Version,
    string? Name,
    string ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string? Body,
    IReadOnlyList<ApplicationUpdateAsset> Assets,
    ApplicationUpdateAsset? PreferredAsset);

public sealed class ApplicationUpdateAsset : IDownloadable, IInstalledDownloadable
{
    public ApplicationUpdateAsset(
        string versionName,
        string name,
        string downloadUrl,
        string downloadFileName,
        string installDirectory,
        long size)
    {
        VersionName = versionName;
        Name = name;
        DownloadUrl = downloadUrl;
        DownloadFileName = downloadFileName;
        InstallDirectory = installDirectory;
        Size = size;
        IsDownloaded = File.Exists(downloadFileName) ||
                       ReleaseVersionPackageManager.IsInstalledDirectory(installDirectory);
    }

    public string VersionName { get; }
    public string Name { get; }
    public string DownloadUrl { get; }
    public bool IsDownloaded { get; set; }
    public bool IsNotAllowDelete => false;
    public string? DownloadFileName { get; }
    public string? DownloadDirectory => null;
    public string InstallDirectory { get; }
    public string? InstalledPath => InstallDirectory;
    public long Size { get; }
}
