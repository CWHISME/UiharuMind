using Avalonia.Controls;
using Avalonia.Interactivity;
using UiharuMind.Services;
using UiharuMind.Views.Common;
using UiharuMind.ViewModels;
using UiharuMind.ViewModels.Pages;
using UiharuMind.Views.Pages;
using UiharuMind.Views.SettingViews;

namespace UiharuMind.Views.Windows;

public partial class SettingsWindow : UiharuWindowBase
{
    private readonly GeneralSettingView _generalSettingView = new();
    private readonly RuntimeEngineSettingView _runtimeEngineSettingView = new();
    private readonly LLamaCppSettingView _llamaCppSettingView = new();
    private readonly ShortcutSettingView _shortcutSettingView = new();
    private readonly HelpPageData _helpPageData = (HelpPageData)App.ViewModel.GetPage(MenuPages.MenuHelpKey);
    private readonly AboutPage _aboutPage = new();

    public override bool IsCacheWindow => true;

    public SettingsWindow()
    {
        InitializeComponent();
        VersionText.Text = App.Version.ToString();
        LocalizationManager.Instance.LanguageChanged += RefreshTitle;
        SelectGeneral();
    }

    private void OnGeneralClick(object? sender, RoutedEventArgs e)
    {
        SelectGeneral();
    }

    private void OnRuntimeClick(object? sender, RoutedEventArgs e)
    {
        Select(RuntimeButton, _runtimeEngineSettingView, "RuntimeEngineSetting");
    }

    private void OnModelClick(object? sender, RoutedEventArgs e)
    {
        Select(ModelButton, _llamaCppSettingView, "LLamaCppSetting");
    }

    private void OnShortcutsClick(object? sender, RoutedEventArgs e)
    {
        Select(ShortcutsButton, _shortcutSettingView, "ShortcutsSetting");
    }

    private void OnHelpClick(object? sender, RoutedEventArgs e)
    {
        Select(HelpButton, _helpPageData.View, "TrayMenuHelp");
    }

    private void OnAboutClick(object? sender, RoutedEventArgs e)
    {
        Select(AboutButton, _aboutPage, "TrayMenuAbout");
    }

    private void SelectGeneral()
    {
        Select(GeneralButton, _generalSettingView, "GeneralSetting");
    }

    private string _selectedTitleKey = "GeneralSetting";

    private void Select(Button selectedButton, Control content, string titleKey)
    {
        _selectedTitleKey = titleKey;
        GeneralButton.Classes.Set("selected", selectedButton == GeneralButton);
        RuntimeButton.Classes.Set("selected", selectedButton == RuntimeButton);
        ModelButton.Classes.Set("selected", selectedButton == ModelButton);
        ShortcutsButton.Classes.Set("selected", selectedButton == ShortcutsButton);
        HelpButton.Classes.Set("selected", selectedButton == HelpButton);
        AboutButton.Classes.Set("selected", selectedButton == AboutButton);
        SettingsContent.Content = content;
        RefreshTitle();
    }

    private void RefreshTitle()
    {
        TitleText.Text = LocalizationManager.Instance.GetString(_selectedTitleKey);
    }
}
