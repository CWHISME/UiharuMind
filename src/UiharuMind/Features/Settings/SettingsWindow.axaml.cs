using Avalonia.LogicalTree;
using Avalonia.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;
using UiharuMind.Features.About;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Features.Settings;

public partial class SettingsWindow : UiharuWindowBase
{
    private readonly GeneralSettingView _generalSettingView;
    private readonly RuntimeEngineSettingView _runtimeEngineSettingView;
    private readonly ShortcutSettingView _shortcutSettingView;
    private readonly AgentSettingView _agentSettingView;
    private readonly QuickToolSettingView _quickToolSettingView;
    private readonly HelpPageData _helpPageData;
    private readonly AboutPage _aboutPage = new();

    private readonly List<SettingsPageEntry> _pages = [];
    private string _selectedTitleKey = "GeneralSetting";

    public override bool IsCacheWindow => true;

    // UIManager 按 new() 建窗走这条：页数据与取页路径统一从 App.ViewModel 那份缓存拿，行为不变
    public SettingsWindow() : this(
        App.ViewModel.GetViewModel<GeneralSettingViewModel>(),
        App.ViewModel.GetViewModel<SettingViewModel>().RuntimeEngineSettingData,
        App.ViewModel.GetViewModel<ShortcutSettingViewModel>(),
        App.ViewModel.GetPage)
    {
    }

    /// <summary>
    /// 可注入组装。无头 App 下 <c>App.ViewModel</c> 为 null（HeadlessTestApp 不跑真 App 的组装），
    /// 测试自备页数据与取页路径；生产路径仍走无参构造，两路渲染同一批页面。
    /// </summary>
    /// <param name="generalSetting">General 页数据</param>
    /// <param name="runtimeEngineSetting">Runtime 页数据</param>
    /// <param name="shortcutSetting">Shortcut 页数据</param>
    /// <param name="getPage">取页路径（本窗只取 MenuHelpKey）</param>
    public SettingsWindow(
        GeneralSettingViewModel generalSetting,
        RuntimeEngineSettingData runtimeEngineSetting,
        ShortcutSettingViewModel shortcutSetting,
        Func<MenuPages, PageDataBase> getPage)
    {
        _generalSettingView = new GeneralSettingView(generalSetting);
        _runtimeEngineSettingView = new RuntimeEngineSettingView(runtimeEngineSetting);
        _shortcutSettingView = new ShortcutSettingView(shortcutSetting);
        _agentSettingView = new AgentSettingView();
        _quickToolSettingView = new QuickToolSettingView();
        _helpPageData = (HelpPageData)getPage(MenuPages.MenuHelpKey);
        InitializeComponent();
        VersionText.Text = App.Version.ToString();
        EngineUpdateDot.Bind(IsVisibleProperty,
            new Binding(nameof(RuntimeEngineSettingData.HasEngineUpdate)) { Source = runtimeEngineSetting });
        LocalizationManager.Instance.LanguageChanged += RefreshTitle;

        // 页表是本窗导航、标题、搜索三处的唯一真相源：加页只在这一个地方加一行
        _pages.Add(new SettingsPageEntry(GeneralButton, _generalSettingView, "GeneralSetting"));
        _pages.Add(new SettingsPageEntry(ShortcutsButton, _shortcutSettingView, "ShortcutsSetting"));
        _pages.Add(new SettingsPageEntry(QuickToolButton, _quickToolSettingView, "QuickToolSetting"));
        _pages.Add(new SettingsPageEntry(AgentButton, _agentSettingView, "AgentSetting"));
        _pages.Add(new SettingsPageEntry(RuntimeButton, _runtimeEngineSettingView, "RuntimeEngineSetting"));
        _pages.Add(new SettingsPageEntry(HelpButton, _helpPageData.View, "TrayMenuHelp"));
        _pages.Add(new SettingsPageEntry(AboutButton, _aboutPage, "TrayMenuAbout"));

        RestoreWindowSize();
        Select(_pages[0]);
    }

    private void OnGeneralClick(object? sender, RoutedEventArgs e) => SelectByButton(GeneralButton);

    private void OnRuntimeClick(object? sender, RoutedEventArgs e) => SelectByButton(RuntimeButton);

    private void OnShortcutsClick(object? sender, RoutedEventArgs e) => SelectByButton(ShortcutsButton);

    private void OnQuickToolClick(object? sender, RoutedEventArgs e) => SelectByButton(QuickToolButton);

    private void OnAgentClick(object? sender, RoutedEventArgs e) => SelectByButton(AgentButton);

    private void OnHelpClick(object? sender, RoutedEventArgs e) => SelectByButton(HelpButton);

    private void OnAboutClick(object? sender, RoutedEventArgs e) => SelectByButton(AboutButton);

    private SettingsPageEntry? FindByButton(Button button) =>
        _pages.FirstOrDefault(page => page.NavButton == button);

    /// <summary>按导航按钮切页（导航栏点击走这条）</summary>
    private void SelectByButton(Button button)
    {
        if (FindByButton(button) is { } page) Select(page);
    }

    /// <summary>
    /// 切到「本地模型」页并把下载源一节滚进视野（模型页跳下载源设置走这条）
    /// </summary>
    public void ShowDownloadSourceSettings()
    {
        SelectByButton(RuntimeButton);
        // 切页后那一节才挂进可视树，等这一轮布局走完再滚
        Dispatcher.UIThread.Post(() => _runtimeEngineSettingView.GetLogicalDescendants()
            .OfType<DownloadSourceSettingsView>().FirstOrDefault()?.BringIntoView());
    }

    /// <summary>按标题 key 切页（搜索结果跳转走这条）</summary>
    private void SelectByTitleKey(string titleKey)
    {
        if (_pages.FirstOrDefault(page => page.TitleKey == titleKey) is { } page) Select(page);
    }

    private void Select(SettingsPageEntry page)
    {
        _selectedTitleKey = page.TitleKey;
        foreach (SettingsPageEntry entry in _pages)
        {
            entry.NavButton.Classes.Set("selected", entry == page);
        }

        SettingsContent.Content = page.Page;
        RefreshTitle();
    }

    private void RefreshTitle()
    {
        TitleText.Text = LocalizationManager.Instance.GetString(_selectedTitleKey);
    }

    /// <summary>窗口尺寸记忆：上一会话调过的大小，这次开窗接着用</summary>
    private void RestoreWindowSize()
    {
        SettingConfig setting = ConfigManager.Instance.Setting;
        if (setting.SettingsWindowWidth >= MinWidth) Width = setting.SettingsWindowWidth;
        if (setting.SettingsWindowHeight >= MinHeight) Height = setting.SettingsWindowHeight;
    }

    protected override void OnClosed(EventArgs e)
    {
        LocalizationManager.Instance.LanguageChanged -= RefreshTitle;
        base.OnClosed(e);
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();

        // 关窗（缓存窗口是隐藏）时记一次尺寸。最大化/最小化时量到的不是用户调的大小，跳过
        if (WindowState != WindowState.Normal) return;
        if (Bounds.Width < MinWidth || Bounds.Height < MinHeight) return;

        SettingConfig setting = ConfigManager.Instance.Setting;
        setting.SettingsWindowWidth = Bounds.Width;
        setting.SettingsWindowHeight = Bounds.Height;
        setting.Save();
    }
}

/// <summary>设置窗口里的一页：导航按钮 + 页面内容 + 标题 key（标题、搜索共用）</summary>
/// <param name="NavButton">左栏按钮</param>
/// <param name="Page">页面内容</param>
/// <param name="TitleKey">本地化 key，同时也是搜索结果的页名</param>
internal sealed record SettingsPageEntry(Button NavButton, Control Page, string TitleKey);
