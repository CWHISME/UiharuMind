using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using UiharuMind.Shared.Controls;
using UiharuMind.Shared.Markup;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;
using UiharuMind.Features.About;
using UiharuMind.Features.Settings.Audio;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;

namespace UiharuMind.Features.Settings;

public partial class SettingsWindow : UiharuWindowBase
{
    private const string UpdateDotClass = "status-dot";

    private readonly List<SettingsPageEntry> _pages = [];
    private string _selectedTitleKey = "GeneralSetting";

    public override bool IsCacheWindow => true;

    // UIManager 按 new() 建窗走这条：页数据与取页路径统一从 App.ViewModel 那份缓存拿，行为不变
    public SettingsWindow() : this(
        App.ViewModel.GetViewModel<GeneralSettingViewModel>(),
        [
            EngineSettingsPage.Text(App.ViewModel.GetViewModel<SettingViewModel>().RuntimeEngineSettingData),
            EngineSettingsPage.Audio(App.ViewModel.GetViewModel<SettingViewModel>().AudioEngineSettingData)
        ],
        App.ViewModel.GetViewModel<ShortcutSettingViewModel>(),
        App.ViewModel.GetPage)
    {
    }

    /// <summary>
    /// 可注入组装。无头 App 下 <c>App.ViewModel</c> 为 null（HeadlessTestApp 不跑真 App 的组装），
    /// 测试自备页数据与取页路径；生产路径仍走无参构造，两路渲染同一批页面。
    /// </summary>
    /// <param name="generalSetting">General 页数据</param>
    /// <param name="enginePages">各本地引擎的页（排在「智能体」分组里）</param>
    /// <param name="shortcutSetting">Shortcut 页数据</param>
    /// <param name="getPage">取页路径（本窗只取 MenuHelpKey）</param>
    public SettingsWindow(
        GeneralSettingViewModel generalSetting,
        IReadOnlyList<EngineSettingsPage> enginePages,
        ShortcutSettingViewModel shortcutSetting,
        Func<MenuPages, PageDataBase> getPage)
    {
        InitializeComponent();
        VersionText.Text = App.Version.ToString();
        LocalizationManager.Instance.LanguageChanged += RefreshTitle;

        // 页表是本窗导航、标题、搜索三处的唯一真相源：加页只在这一个地方加一行
        List<SettingsPageSpec> specs =
        [
            new("SettingsNavGroupApp", "GeneralButton", "settings", "GeneralSetting", new GeneralSettingView(generalSetting)),
            new("SettingsNavGroupApp", "ShortcutsButton", "keyboard", "ShortcutsSetting", new ShortcutSettingView(shortcutSetting)),
            new("SettingsNavGroupApp", "QuickToolButton", "message-circle-more", "QuickToolSetting", new QuickToolSettingView()),
            new("SettingsNavGroupAgent", "AgentButton", "bot", "AgentSetting", new AgentSettingView()),
            ..enginePages.Select(x => new SettingsPageSpec("SettingsNavGroupAgent", x.NavName, x.IconName, x.TitleKey,
                x.View, x.Engine, x.UpdateTipKey)),
            new("SettingsNavGroupAgent", "DownloadSourceButton", "download", "DownloadSourceSetting", new DownloadSourceSettingsView()),
            new("SettingsNavGroupHelp", "HelpButton", "book-search", "TrayMenuHelp", ((HelpPageData)getPage(MenuPages.MenuHelpKey)).View),
            new("SettingsNavGroupHelp", "AboutButton", "info", "TrayMenuAbout", new AboutPage())
        ];
        BuildNavigation(specs);

        RestoreWindowSize();
        Select(_pages[0]);
    }

    /// <summary>
    /// 切到下载源页（模型页、语音页跳下载源设置走这条）
    /// </summary>
    public void ShowDownloadSourceSettings() => SelectByTitleKey("DownloadSourceSetting");

    /// <summary>直接切到「本地模型（语音）」页（模型页的语音模型页签跳过来时用）</summary>
    public void ShowAudioEngineSettings() => SelectByTitleKey("AudioEngineSetting");

    /// <summary>
    /// 按名字取左栏按钮（如 RuntimeButton）；没有为 null
    /// </summary>
    /// <param name="navName">按钮名</param>
    internal Button? NavButton(string navName) => _pages.FirstOrDefault(x => x.NavButton.Name == navName)?.NavButton;

    // 按页表建左栏：同组的排在一个标题下，引擎页挂「有新版本」的点
    private void BuildNavigation(IReadOnlyList<SettingsPageSpec> specs)
    {
        foreach (IGrouping<string, SettingsPageSpec> group in specs.GroupBy(x => x.GroupKey))
        {
            StackPanel panel = new() { Spacing = 2 };
            TextBlock header = new() { Classes = { "SettingsNavGroup" } };
            header.Bind(TextBlock.TextProperty, Localized(group.Key));
            panel.Children.Add(header);
            foreach (SettingsPageSpec spec in group)
            {
                Button button = CreateNavButton(spec);
                panel.Children.Add(button);
                _pages.Add(new SettingsPageEntry(button, spec.Page, spec.TitleKey));
            }

            NavHost.Children.Add(panel);
        }
    }

    private Button CreateNavButton(SettingsPageSpec spec)
    {
        TextBlock title = new();
        title.Bind(TextBlock.TextProperty, Localized(spec.TitleKey));
        StackPanel content = new()
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Children = { new ThemedSvgIcon { Width = 16, Height = 16, IconName = spec.IconName }, title }
        };
        if (spec.Engine != null)
        {
            Border dot = new() { Classes = { UpdateDotClass }, Tag = "Progress" };
            dot.Bind(IsVisibleProperty, new Binding(nameof(EngineVersionsViewData.HasUpdate)) { Source = spec.Engine });
            if (spec.UpdateTipKey != null) dot.Bind(ToolTip.TipProperty, Localized(spec.UpdateTipKey));
            content.Children.Add(dot);
        }

        Button button = new() { Name = spec.NavName, Classes = { "SettingsNavButton" }, Content = content };
        button.Click += (_, _) => SelectByButton(button);
        return button;
    }

    private static Binding Localized(string key) =>
        new(nameof(LocalizedString.Value)) { Source = LocalizedString.Get(key, null) };

    private SettingsPageEntry? FindByButton(Button button) =>
        _pages.FirstOrDefault(page => page.NavButton == button);

    /// <summary>按导航按钮切页（导航栏点击走这条）</summary>
    private void SelectByButton(Button button)
    {
        if (FindByButton(button) is { } page) Select(page);
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

/// <summary>页表里的一行：分组、按钮名、图标、标题 key、页面，引擎页另带引擎卡（更新点）与它的说明</summary>
internal sealed record SettingsPageSpec(
    string GroupKey,
    string NavName,
    string IconName,
    string TitleKey,
    Control Page,
    EngineVersionsViewData? Engine = null,
    string? UpdateTipKey = null);

/// <summary>设置窗口里的一页：导航按钮 + 页面内容 + 标题 key（标题、搜索共用）</summary>
/// <param name="NavButton">左栏按钮</param>
/// <param name="Page">页面内容</param>
/// <param name="TitleKey">本地化 key，同时也是搜索结果的页名</param>
internal sealed record SettingsPageEntry(Button NavButton, Control Page, string TitleKey);
