using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Input;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.Views.SettingViews;

public partial class ShortcutSettingView : UserControl
{
    public ShortcutSettingView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<ShortcutSettingViewModel>();
    }

    private async void OnCaptureScreenShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShortcutSettingViewModel viewModel) return;
        viewModel.CaptureScreenShortcut = await CaptureShortcut(viewModel.CaptureScreenShortcut);
    }

    private async void OnQuickStartChatShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShortcutSettingViewModel viewModel) return;
        viewModel.QuickStartChatShortcut = await CaptureShortcut(viewModel.QuickStartChatShortcut);
    }

    private async void OnClipboardHistoryShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShortcutSettingViewModel viewModel) return;
        viewModel.ClipboardHistoryShortcut = await CaptureShortcut(viewModel.ClipboardHistoryShortcut);
    }

    private async void OnQuickTranslationShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShortcutSettingViewModel viewModel) return;
        viewModel.QuickTranslationShortcut = await CaptureShortcut(viewModel.QuickTranslationShortcut);
    }

    private async void OnQuickAutoClickShortcutClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ShortcutSettingViewModel viewModel) return;
        viewModel.QuickAutoClickShortcut = await CaptureShortcut(viewModel.QuickAutoClickShortcut);
    }

    private async Task<string> CaptureShortcut(string fallback)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return fallback;
        var result = await KeySelectionWindow.ShowShortcutDialog(owner);
        return result?.DisplayText ?? fallback;
    }
}

public partial class ShortcutSettingViewModel : ViewModelBase
{
    [ObservableProperty] private string _captureScreenShortcut = string.Empty;
    [ObservableProperty] private string _quickStartChatShortcut = string.Empty;
    [ObservableProperty] private string _clipboardHistoryShortcut = string.Empty;
    [ObservableProperty] private string _quickTranslationShortcut = string.Empty;
    [ObservableProperty] private string _quickAutoClickShortcut = string.Empty;
    [ObservableProperty] private string _statusText = string.Empty;

    public ShortcutSettingViewModel()
    {
        LoadFromConfig();
        LocalizationManager.Instance.LanguageChanged += RefreshStatusLanguage;
    }

    [RelayCommand]
    private void Apply()
    {
        var shortcuts = new[]
        {
            new ShortcutEditItem("ShortcutCaptureScreen", CaptureScreenShortcut),
            new ShortcutEditItem("ShortcutQuickStartChat", QuickStartChatShortcut),
            new ShortcutEditItem("ShortcutClipboardHistory", ClipboardHistoryShortcut),
            new ShortcutEditItem("ShortcutQuickTranslation", QuickTranslationShortcut),
            new ShortcutEditItem("ShortcutQuickAutoClick", QuickAutoClickShortcut),
        };

        var normalized = new Dictionary<string, string>();
        foreach (var shortcut in shortcuts)
        {
            if (!ShortcutGestureParser.TryParse(shortcut.Value, out var mainKey, out var modifiers, out var error))
            {
                StatusText = $"{LocalizationManager.Instance.GetString(shortcut.TitleKey)}: {error}";
                return;
            }

            var display = ShortcutGestureParser.ToDisplayString(mainKey, modifiers);
            if (normalized.ContainsKey(display))
            {
                StatusText = LocalizationManager.Instance.GetString("ShortcutConflictTips");
                return;
            }

            normalized[display] = shortcut.TitleKey;
        }

        var setting = ConfigManager.Instance.Setting;
        setting.CaptureScreenShortcut = ShortcutGestureParser.Normalize(CaptureScreenShortcut);
        setting.QuickStartChatShortcut = ShortcutGestureParser.Normalize(QuickStartChatShortcut);
        setting.ClipboardHistoryShortcut = ShortcutGestureParser.Normalize(ClipboardHistoryShortcut);
        setting.QuickTranslationShortcut = ShortcutGestureParser.Normalize(QuickTranslationShortcut);
        setting.QuickAutoClickShortcut = ShortcutGestureParser.Normalize(QuickAutoClickShortcut);
        setting.Save();

        LoadFromConfig();
        App.DummyWindow.ReloadShortcuts();
        StatusText = LocalizationManager.Instance.GetString("ShortcutSavedTips");
    }

    [RelayCommand]
    private void ResetDefaults()
    {
        CaptureScreenShortcut = SettingConfig.DefaultCaptureScreenShortcut;
        QuickStartChatShortcut = SettingConfig.DefaultQuickStartChatShortcut;
        ClipboardHistoryShortcut = SettingConfig.DefaultClipboardHistoryShortcut;
        QuickTranslationShortcut = SettingConfig.DefaultQuickTranslationShortcut;
        QuickAutoClickShortcut = SettingConfig.DefaultQuickAutoClickShortcut;
        Apply();
    }

    private void LoadFromConfig()
    {
        var setting = ConfigManager.Instance.Setting;
        CaptureScreenShortcut = setting.CaptureScreenShortcut;
        QuickStartChatShortcut = setting.QuickStartChatShortcut;
        ClipboardHistoryShortcut = setting.ClipboardHistoryShortcut;
        QuickTranslationShortcut = setting.QuickTranslationShortcut;
        QuickAutoClickShortcut = setting.QuickAutoClickShortcut;
    }

    private void RefreshStatusLanguage()
    {
        if (string.IsNullOrWhiteSpace(StatusText)) return;
        StatusText = string.Empty;
    }

    private readonly record struct ShortcutEditItem(string TitleKey, string Value);
}
