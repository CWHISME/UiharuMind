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
    [ObservableProperty] private bool _enableFullscreenGameInputSupport;

    /// <summary>
    /// 剪贴板历史自动清理的保留天数，0 = 不清理（默认）。收藏项永远豁免。
    /// <b>改完在下次启动时生效</b>——刻意没有「立即清理」按钮：为省一次重启而配一套
    /// 确认弹窗与结果提示，不划算。
    /// 直接读写配置而不进 <c>_writeBack</c>：那道闸是给 DebugSetting 那几项用的
    /// </summary>
    public decimal ClipboardRetentionDays
    {
        get => ConfigManager.Instance.Setting.ClipboardRetentionDays;
        set
        {
            ConfigManager.Instance.Setting.ClipboardRetentionDays = (int)value;
            OnPropertyChanged();
        }
    }

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

        //从配置回填界面:此前这里一进设置页就把 DebugSetting 原样重写一遍落盘,
        //全屏输入那一项还得靠自带的 _isInitialized 挡住回填触发的重启确认弹窗
        using (_writeBack.BeginLoad())
        {
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
