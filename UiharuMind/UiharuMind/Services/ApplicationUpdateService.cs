using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private readonly ApplicationUpdateVersionManager _versionManager = new();

    [ObservableProperty] private bool _isCheckingForUpdates;
    [ObservableProperty] private bool _hasChecked;
    [ObservableProperty] private string? _lastError;
    [ObservableProperty] private ManagedVersionPackage? _latestPackage;

    public bool HasAvailableUpdate =>
        LatestPackage != null && ManagedVersionPackage.IsRemoteVersionNewer(LatestPackage.Version, App.Version);

    public string LatestReleaseUrl => LatestPackage?.ReleaseUrl ?? LatestReleasePageUrl;

    partial void OnLatestPackageChanged(ManagedVersionPackage? value)
    {
        OnPropertyChanged(nameof(HasAvailableUpdate));
        OnPropertyChanged(nameof(LatestReleaseUrl));
    }

    public async Task CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        if (!await _checkLock.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            IsCheckingForUpdates = true;
            LastError = null;
            LatestPackage = await FetchLatestPackageAsync(cancellationToken).ConfigureAwait(false);
            HasChecked = true;
            OnPropertyChanged(nameof(HasAvailableUpdate));
            OnPropertyChanged(nameof(LatestReleaseUrl));
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

    private async Task<ManagedVersionPackage?> FetchLatestPackageAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<ManagedVersionPackage> assets = await _versionManager
            .PullLatestVersionsAsync(GetUpdatePackageRoot(), cancellationToken)
            .ConfigureAwait(false);
        return assets.FirstOrDefault();
    }

    public async Task InstallPackageAsync(
        ManagedVersionPackage asset,
        CancellationToken cancellationToken = default)
    {
        await _versionManager.InstallArchiveAsync(asset, true, cancellationToken)
            .ConfigureAwait(false);
    }

    private static string GetUpdatePackageRoot()
    {
        string directory = Path.Combine(SettingConfig.SaveDataPath, "ApplicationUpdates");
        Directory.CreateDirectory(directory);
        return directory;
    }
}
