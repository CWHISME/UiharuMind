using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Features.Settings;

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

    //写回闸门。这一页的写回都落在 DebugSetting 上,回填(构造、切语言)期间静默
    private readonly SettingsWriteBack _writeBack = new(() => ConfigManager.Instance.DebugSetting.Save());

    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private string[] _logLevelList;
    [ObservableProperty] private int _logSelectedTypeIndex;
    [ObservableProperty] private bool _enableFullscreenGameInputSupport;
    [ObservableProperty] private bool _isCheckingForAppUpdate;
    [ObservableProperty] private bool _hasAppUpdate;
    [ObservableProperty] private bool _hasAppUpdateError;
    [ObservableProperty] private string _appUpdateStatusText = "";
    [ObservableProperty] private string _latestVersionText = "";
    [ObservableProperty] private string _appUpdateErrorText = "";
    [ObservableProperty] private bool _hasApplicationUpdateAsset;

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
            DownloadedActionText = Loc.Text("ApplicationUpdateInstall"),
            DownloadedActionHandler = InstallApplicationUpdateAsync,
            DeleteConfirmMessageProvider = () => Loc.Text("ConfirmDeleteApplicationUpdate")
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

        //从配置回填界面:此前这里一进设置页就把 DebugSetting 原样重写一遍落盘,
        //全屏输入那一项还得靠自带的 _isInitialized 挡住回填触发的重启确认弹窗
        using (_writeBack.BeginLoad())
        {
            LogSelectedTypeIndex = (int)ConfigManager.Instance.DebugSetting.LogTypeInfo;
            EnableFullscreenGameInputSupport = ConfigManager.Instance.Setting.EnableFullscreenGameInputSupport;
            RefreshThemeOptions();
            RefreshSelectedLanguage();
        }

        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;
        _applicationUpdateService.PropertyChanged += OnApplicationUpdateServicePropertyChanged;
        RefreshApplicationUpdateState();
        _ = EnsureApplicationUpdateCheckedAsync();
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

    partial void OnLogSelectedTypeIndexChanged(int value)
    {
        ConfigManager.Instance.DebugSetting.LogTypeInfo = (ELogType)value;
        _writeBack.Save();
    }

    async partial void OnEnableFullscreenGameInputSupportChanged(bool value)
    {
        if (_writeBack.IsLoading) return;
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
            new Uri(_applicationUpdateService.LatestReleaseUrl));
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
        //切语言后重建选项并重新选中,同样是回填而非用户改动
        using (_writeBack.BeginLoad())
        {
            RefreshThemeOptions();
            RefreshSelectedLanguage();
        }

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
        LatestVersionText = _applicationUpdateService.LatestPackage?.Name ?? "-";
        HasApplicationUpdateAsset = _applicationUpdateService.LatestPackage != null;
        SetApplicationUpdateAsset(HasAppUpdate ? _applicationUpdateService.LatestPackage : null);

        if (IsCheckingForAppUpdate)
        {
            AppUpdateStatusText = LocalizationManager.Instance.GetString("CheckingForUpdates");
            return;
        }

        if (HasAppUpdate && _applicationUpdateService.LatestPackage != null)
        {
            AppUpdateStatusText = string.Format(
                LocalizationManager.Instance.GetString("ApplicationUpdateAvailableFormat"),
                _applicationUpdateService.LatestPackage.Name);
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

    private void SetApplicationUpdateAsset(ManagedVersionPackage? asset)
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
            App.FilesService.OpenFolder(((ManagedVersionPackage)item.Target).InstallDirectory);
            return;
        }

        if (!File.Exists(item.DownloadFilePath))
        {
            await _messageService.ShowWarningAsync(Loc.Text("ApplicationUpdateInstallFileMissing"));
            return;
        }

        if (!await _messageService.ConfirmAsync(Loc.Text("ApplicationUpdateInstallConfirm")))
        {
            return;
        }

        try
        {
            if (item.Target is not ManagedVersionPackage asset)
            {
                await _messageService.ShowWarningAsync(Loc.Text("ApplicationUpdateInstallFileMissing"));
                return;
            }

            await _applicationUpdateService.InstallPackageAsync(asset);
            item.IsDownloaded = true;
            item.InitFileSize();
            UpdateApplicationUpdateDownloadedActionText(asset);
            App.FilesService.OpenFolder(asset.InstallDirectory);
            _messageService.ShowNotification(
                Loc.Text("ApplicationUpdateInstallPackageDeleted"),
                severity: MessageSeverity.Success);
        }
        catch (Exception e)
        {
            Log.Error(e);
            await _messageService.ShowWarningAsync(e.Message, Loc.Text("ApplicationUpdateInstallFailed"));
        }
    }

    private void UpdateApplicationUpdateDownloadedActionText(ManagedVersionPackage? asset)
    {
        ApplicationUpdateDownloadListViewModel.DownloadedActionText =
            asset is { IsInstalled: true }
                ? Loc.Text("OpenDirectory")
                : Loc.Text("ApplicationUpdateInstall");
    }

    private static bool IsApplicationUpdateInstalled(DownloadableItemData item)
    {
        return item.Target is ManagedVersionPackage { IsInstalled: true };
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
