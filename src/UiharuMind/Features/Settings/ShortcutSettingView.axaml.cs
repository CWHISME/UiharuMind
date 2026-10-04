using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Input;
using UiharuMind.Generated;

namespace UiharuMind.Features.Settings;

public partial class ShortcutSettingView : UserControl
{
    public ShortcutSettingView() : this(App.ViewModel.GetViewModel<ShortcutSettingViewModel>())
    {
    }

    /// <summary>
    /// 可注入页数据。无头 App 下 App.ViewModel 为 null，测试用它组装；生产仍走无参构造。
    /// </summary>
    /// <param name="viewModel">Shortcut 页数据</param>
    public ShortcutSettingView(ShortcutSettingViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
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

    // 「值≠出厂值」才显示单项恢复默认按钮（SettingsRow 可见性绑定用）；出厂值引用 SettingConfig 常量
    public bool IsCaptureScreenShortcutNotDefault => CaptureScreenShortcut != SettingConfig.DefaultCaptureScreenShortcut;
    public bool IsQuickStartChatShortcutNotDefault => QuickStartChatShortcut != SettingConfig.DefaultQuickStartChatShortcut;
    public bool IsClipboardHistoryShortcutNotDefault => ClipboardHistoryShortcut != SettingConfig.DefaultClipboardHistoryShortcut;
    public bool IsQuickTranslationShortcutNotDefault => QuickTranslationShortcut != SettingConfig.DefaultQuickTranslationShortcut;
    public bool IsQuickAutoClickShortcutNotDefault => QuickAutoClickShortcut != SettingConfig.DefaultQuickAutoClickShortcut;

    partial void OnCaptureScreenShortcutChanged(string value) => OnPropertyChanged(nameof(IsCaptureScreenShortcutNotDefault));
    partial void OnQuickStartChatShortcutChanged(string value) => OnPropertyChanged(nameof(IsQuickStartChatShortcutNotDefault));
    partial void OnClipboardHistoryShortcutChanged(string value) => OnPropertyChanged(nameof(IsClipboardHistoryShortcutNotDefault));
    partial void OnQuickTranslationShortcutChanged(string value) => OnPropertyChanged(nameof(IsQuickTranslationShortcutNotDefault));
    partial void OnQuickAutoClickShortcutChanged(string value) => OnPropertyChanged(nameof(IsQuickAutoClickShortcutNotDefault));

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
            new ShortcutEditItem(LangKey.ShortcutCaptureScreen, CaptureScreenShortcut),
            new ShortcutEditItem(LangKey.ShortcutQuickStartChat, QuickStartChatShortcut),
            new ShortcutEditItem(LangKey.ShortcutClipboardHistory, ClipboardHistoryShortcut),
            new ShortcutEditItem(LangKey.ShortcutQuickTranslation, QuickTranslationShortcut),
            new ShortcutEditItem(LangKey.ShortcutQuickAutoClick, QuickAutoClickShortcut),
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
                StatusText = Loc.Text(LangKey.ShortcutConflictTips);
                return;
            }

            normalized[display] = shortcut.TitleKey.ToString();
        }

        var setting = ConfigManager.Instance.Setting;
        setting.CaptureScreenShortcut = ShortcutGestureParser.Normalize(CaptureScreenShortcut);
        setting.QuickStartChatShortcut = ShortcutGestureParser.Normalize(QuickStartChatShortcut);
        setting.ClipboardHistoryShortcut = ShortcutGestureParser.Normalize(ClipboardHistoryShortcut);
        setting.QuickTranslationShortcut = ShortcutGestureParser.Normalize(QuickTranslationShortcut);
        setting.QuickAutoClickShortcut = ShortcutGestureParser.Normalize(QuickAutoClickShortcut);
        setting.Save();

        LoadFromConfig();
        // 无头/未初始化环境没有 DummyWindow，跳过重载（生产路径 DummyWindow 恒在，行为不变）
        App.DummyWindow?.ReloadShortcuts();
        StatusText = Loc.Text(LangKey.ShortcutSavedTips);
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

    // 单项恢复默认：每行自己的「恢复默认」按钮。设回默认值后直接 Apply，
    // 与整体 ResetDefaults 同一条生效路径（校验+冲突检测+写配置+重载快捷键）
    [RelayCommand]
    private void ResetCaptureScreenShortcut()
    {
        CaptureScreenShortcut = SettingConfig.DefaultCaptureScreenShortcut;
        Apply();
    }

    [RelayCommand]
    private void ResetQuickStartChatShortcut()
    {
        QuickStartChatShortcut = SettingConfig.DefaultQuickStartChatShortcut;
        Apply();
    }

    [RelayCommand]
    private void ResetClipboardHistoryShortcut()
    {
        ClipboardHistoryShortcut = SettingConfig.DefaultClipboardHistoryShortcut;
        Apply();
    }

    [RelayCommand]
    private void ResetQuickTranslationShortcut()
    {
        QuickTranslationShortcut = SettingConfig.DefaultQuickTranslationShortcut;
        Apply();
    }

    [RelayCommand]
    private void ResetQuickAutoClickShortcut()
    {
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

    private readonly record struct ShortcutEditItem(LangKey TitleKey, string Value);
}
