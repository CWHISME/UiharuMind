using System;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Services;
using UiharuMind.ViewModels;

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
    [ObservableProperty] private LanguageOption? _selectedLanguage;
    [ObservableProperty] private ThemeOption? _selectedTheme;
    [ObservableProperty] private string[] _logLevelList;
    [ObservableProperty] private int _logSelecetedTypeIndex;

    public ObservableCollection<LanguageOption> LanguageOptions { get; } = new();
    public ObservableCollection<ThemeOption> ThemeOptions { get; } = new();

    public string VersionText => $"UiharuMind {App.Version}";
    public string SaveDirectoryPath => SettingConfig.SaveDataPath;

    public GeneralSettingViewModel()
    {
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
        RefreshThemeOptions();
        RefreshSelectedLanguage();
        LocalizationManager.Instance.LanguageChanged += RefreshLanguage;
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

    [RelayCommand]
    private void OpenSaveFolder()
    {
        App.FilesService.OpenFolder(SettingConfig.SaveDataPath);
    }

    [RelayCommand]
    private void OpenUpdatePage()
    {
        TopLevel.GetTopLevel(App.DummyWindow)!.Launcher.LaunchUriAsync(
            new Uri("https://github.com/CWHISME/UiharuMind/releases/latest"));
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
