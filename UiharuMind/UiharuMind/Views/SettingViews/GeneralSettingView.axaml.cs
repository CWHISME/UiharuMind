using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.ViewData.Download;
using UiharuMind.Views;

namespace UiharuMind.Views.SettingViews;

public partial class GeneralSettingView : UserControl
{
    public GeneralSettingView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<GeneralSettingViewModel>();
    }
}

public partial class GeneralSettingViewModel : ViewModelBase
{
    private readonly IMessageService _messageService;
    private readonly ApplicationUpdateService _applicationUpdateService;
    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private string[] _logLevelList;
    [ObservableProperty] private int _logSelecetedTypeIndex;
    [ObservableProperty] private bool _enableFullscreenGameInputSupport;
    [ObservableProperty] private bool _isCheckingForAppUpdate;
    [ObservableProperty] private bool _hasAppUpdate;
    [ObservableProperty] private bool _hasAppUpdateError;
    [ObservableProperty] private string _appUpdateStatusText = "";
    [ObservableProperty] private string _latestVersionText = "";
    [ObservableProperty] private string _appUpdateErrorText = "";
    [ObservableProperty] private bool _hasApplicationUpdateAsset;

    private bool _isInitialized;

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();
    public DownloadListViewData ApplicationUpdateDownloadListViewModel { get; }

    public string VersionText => $"UiharuMind {App.Version}";
    public string SaveDirectoryPath => SettingConfig.SaveDataPath;
    public bool IsWindows => PlatformUtils.IsWindows;

    public GeneralSettingViewModel() : this(
        App.Services.GetRequiredService<IMessageService>(),
        App.Services.GetRequiredService<ApplicationUpdateService>())
    {
    }

    public GeneralSettingViewModel(
        IMessageService messageService,
        ApplicationUpdateService applicationUpdateService)
    {
        _messageService = messageService;
        _applicationUpdateService = applicationUpdateService;
        ApplicationUpdateDownloadListViewModel = new DownloadListViewData(messageService)
        {
            DownloadedActionText = L("ApplicationUpdateInstall"),
            DownloadedActionHandler = InstallApplicationUpdateAsync,
            DeleteConfirmMessageProvider = () => L("ConfirmDeleteApplicationUpdate")
        };
        foreach (var cultureInfo in LanguageUtils.SupportedLanguages)
        {
            LanguageOptions.Add(new LanguageOption(cultureInfo));
        }

        var max = (int)ELogType.Error + 1;
        LogLevelList = new string[max];
        for (var i = 0; i < max; i++)
        {
            LogLevelList[i] = ((ELogType)i).ToString();
        }

        LogSelecetedTypeIndex = (int)ConfigManager.Instance.DebugSetting.LogTypeInfo;
        EnableFullscreenGameInputSupport = ConfigManager.Instance.Setting.EnableFullscreenGameInputSupport;
        RefreshThemeOptions();
        RefreshSelectedLanguage();
        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;
        _applicationUpdateService.PropertyChanged += OnApplicationUpdateServicePropertyChanged;
        RefreshApplicationUpdateState();
        _ = EnsureApplicationUpdateCheckedAsync();
        _isInitialized = true;
    }

    partial void OnSelectedLanguageChanged(LanguageOption? value)
    {
        if (value == null || value.CultureInfo.Name == LocalizationManager.Instance.LanguageCode) return;
        LocalizationManager.Instance.ApplyLanguage(value.CultureInfo.Name, true);
    }

    partial void OnSelectedThemeChanged(ThemeOption? value)
    {
        if (value == null) return;
        ApplicationThemeManager.ApplyTheme(value.ThemeMode, true);
    }

    partial void OnLogSelecetedTypeIndexChanged(int value)
    {
        ConfigManager.Instance.DebugSetting.LogTypeInfo = (ELogType)value;
        ConfigManager.Instance.DebugSetting.Save();
    }

    async partial void OnEnableFullscreenGameInputSupportChanged(bool value)
    {
        if (!_isInitialized) return;
        ConfigManager.Instance.Setting.EnableFullscreenGameInputSupport = value;
        if (await _messageService.ConfirmAsync(
                LocalizationManager.Instance.GetString("FullscreenGameInputRestartConfirm")))
        {
            ApplicationRestartService.Restart();
        }
    }

    [RelayCommand]
    private void OpenSaveFolder()
    {
        App.FilesService.OpenFolder(SettingConfig.SaveDataPath);
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        TopLevel.GetTopLevel(App.DummyWindow)!.Launcher.LaunchUriAsync(
            new Uri(_applicationUpdateService.LatestRelease?.ReleaseUrl ??
                    "https://github.com/CWHISME/UiharuMind/releases/latest"));
    }

    [RelayCommand]
    private async Task CheckApplicationUpdate()
    {
        await _applicationUpdateService.CheckForUpdatesAsync();
        RefreshApplicationUpdateState();
    }

    private void RefreshSelectedLanguage()
    {
        foreach (var languageOption in LanguageOptions)
        {
            if (languageOption.CultureInfo.Name == LocalizationManager.Instance.LanguageCode)
            {
                SelectedLanguage = languageOption;
                return;
            }
        }
    }

    private void RefreshLanguage()
    {
        RefreshThemeOptions();
        RefreshSelectedLanguage();
        RefreshApplicationUpdateState();
    }

    private void RefreshThemeOptions()
    {
        var currentThemeMode = ApplicationThemeManager.NormalizeThemeMode(ConfigManager.Instance.Setting.ThemeMode);
        ThemeOptions.Clear();
        foreach (var themeMode in ApplicationThemeManager.SupportedThemeModes)
        {
            ThemeOptions.Add(new ThemeOption(themeMode, $"ThemeMode{themeMode}"));
        }

        foreach (var themeOption in ThemeOptions)
        {
            if (themeOption.ThemeMode == currentThemeMode)
            {
                SelectedTheme = themeOption;
                return;
            }
        }
    }

    private async Task EnsureApplicationUpdateCheckedAsync()
    {
        if (_applicationUpdateService.HasChecked || _applicationUpdateService.IsCheckingForUpdates) return;
        await _applicationUpdateService.CheckForUpdatesAsync();
    }

    private void OnApplicationUpdateServicePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshApplicationUpdateState();
    }

    private void RefreshApplicationUpdateState()
    {
        IsCheckingForAppUpdate = _applicationUpdateService.IsCheckingForUpdates;
        HasAppUpdate = _applicationUpdateService.HasAvailableUpdate;
        HasAppUpdateError = !string.IsNullOrWhiteSpace(_applicationUpdateService.LastError);
        AppUpdateErrorText = _applicationUpdateService.LastError ?? string.Empty;
        LatestVersionText = _applicationUpdateService.LatestRelease?.TagName ?? "-";
        HasApplicationUpdateAsset = _applicationUpdateService.LatestRelease?.PreferredAsset != null;
        SetApplicationUpdateAsset(HasAppUpdate ? _applicationUpdateService.LatestRelease?.PreferredAsset : null);

        if (IsCheckingForAppUpdate)
        {
            AppUpdateStatusText = LocalizationManager.Instance.GetString("CheckingForUpdates");
            return;
        }

        if (HasAppUpdate && _applicationUpdateService.LatestRelease != null)
        {
            AppUpdateStatusText = string.Format(
                LocalizationManager.Instance.GetString("ApplicationUpdateAvailableFormat"),
                _applicationUpdateService.LatestRelease.TagName);
            return;
        }

        if (HasAppUpdateError)
        {
            AppUpdateStatusText = LocalizationManager.Instance.GetString("ApplicationUpdateCheckFailed");
            return;
        }

        AppUpdateStatusText = _applicationUpdateService.HasChecked
            ? LocalizationManager.Instance.GetString("ApplicationUpdateAlreadyLatest")
            : LocalizationManager.Instance.GetString("ApplicationUpdateAutoCheckPending");
    }

    private void SetApplicationUpdateAsset(ApplicationUpdateAsset? asset)
    {
        ApplicationUpdateDownloadListViewModel.ClearIfNotExists();
        UpdateApplicationUpdateDownloadedActionText(asset);
        if (asset == null || ApplicationUpdateDownloadListViewModel.IsExists(asset.Name)) return;
        ApplicationUpdateDownloadListViewModel.AddItem(new DownloadableItemData(asset, true));
    }

    private async Task InstallApplicationUpdateAsync(DownloadableItemData item)
    {
        if (IsApplicationUpdateInstalled(item))
        {
            App.FilesService.OpenFolder(((ApplicationUpdateAsset)item.Target).InstallDirectory);
            return;
        }

        if (!File.Exists(item.DownloadFilePath))
        {
            await _messageService.ShowWarningAsync(L("ApplicationUpdateInstallFileMissing"));
            return;
        }

        if (!await _messageService.ConfirmAsync(L("ApplicationUpdateInstallConfirm")))
        {
            return;
        }

        try
        {
            if (IsZipPackage(item.DownloadFilePath))
            {
                await ExtractApplicationUpdatePackageAsync(item);
            }
            else
            {
                OpenDownloadedPackage(item.DownloadFilePath);
            }

            if (File.Exists(item.DownloadFilePath)) File.Delete(item.DownloadFilePath);
            item.IsDownloaded = true;
            item.InitFileSize();
            UpdateApplicationUpdateDownloadedActionText(item.Target as ApplicationUpdateAsset);
            _messageService.ShowNotification(
                L("ApplicationUpdateInstallPackageDeleted"),
                severity: MessageSeverity.Success);
        }
        catch (Exception e)
        {
            Log.Error(e);
            await _messageService.ShowWarningAsync(e.Message, L("ApplicationUpdateInstallFailed"));
        }
    }

    private static async Task ExtractApplicationUpdatePackageAsync(DownloadableItemData item)
    {
        string extractPath = item.Target is ApplicationUpdateAsset asset
            ? asset.InstallDirectory
            : GetExtractPath(item.DownloadFilePath);
        if (Directory.Exists(extractPath)) Directory.Delete(extractPath, true);

        // 应用更新发布包目前是 zip，安装动作实际是解压并让用户处理替换。
        await SimpleZipHelper.ExtractZipFile(item.DownloadFilePath, extractPath);
        if (!Directory.Exists(extractPath) || Directory.GetFileSystemEntries(extractPath).Length == 0)
        {
            throw new InvalidOperationException(L("ApplicationUpdateExtractFailed"));
        }

        App.FilesService.OpenFolder(extractPath);
    }

    private static void OpenDownloadedPackage(string filePath)
    {
        if (OperatingSystem.IsMacOS())
        {
            var info = new ProcessStartInfo("open")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add("-W");
            info.ArgumentList.Add(filePath);
            Process.Start(info);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            var info = new ProcessStartInfo("xdg-open")
            {
                UseShellExecute = false
            };
            info.ArgumentList.Add(filePath);
            Process.Start(info);
            return;
        }

        Process.Start(new ProcessStartInfo(Path.GetFullPath(filePath))
        {
            UseShellExecute = true
        });
    }

    private static bool IsZipPackage(string filePath)
    {
        return string.Equals(Path.GetExtension(filePath), ".zip", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExtractPath(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        string name = Path.GetFileNameWithoutExtension(filePath);
        return Path.Combine(directory ?? Path.GetTempPath(), name);
    }

    private static string L(string key)
    {
        return LocalizationManager.Instance.GetString(key);
    }

    private void UpdateApplicationUpdateDownloadedActionText(ApplicationUpdateAsset? asset)
    {
        ApplicationUpdateDownloadListViewModel.DownloadedActionText =
            asset != null && ReleaseVersionPackageManager.IsInstalledDirectory(asset.InstallDirectory)
                ? L("OpenDirectory")
                : L("ApplicationUpdateInstall");
    }

    private static bool IsApplicationUpdateInstalled(DownloadableItemData item)
    {
        return item.Target is ApplicationUpdateAsset asset &&
               ReleaseVersionPackageManager.IsInstalledDirectory(asset.InstallDirectory);
    }
}

public class LanguageOption
{
    public CultureInfo CultureInfo { get; }

    public string DisplayName => $"{CultureInfo.NativeName} ({CultureInfo.Name})";

    public LanguageOption(CultureInfo cultureInfo)
    {
        CultureInfo = cultureInfo;
    }
}

public class ThemeOption
{
    public string ThemeMode { get; }
    public string DisplayName => LocalizationManager.Instance.GetString(_displayNameKey);

    private readonly string _displayNameKey;

    public ThemeOption(string themeMode, string displayNameKey)
    {
        ThemeMode = themeMode;
        _displayNameKey = displayNameKey;
    }
}
