/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System.Linq;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Features.Settings.Engines;
using System;
using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Controls;
using UiharuMind.Features.LogViewer;
using UiharuMind.Features.About;
using UiharuMind.Features.Services;
using UiharuMind.Features.Translate;
using UiharuMind.Features.Models;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Settings;
using UiharuMind.Features.Conversation;
using UiharuMind.Shared.Data;
using UiharuMind.Features.Conversation.Pages;
using UiharuMind.Shared.Diagnostics;
using UiharuMind.Features.AutoClick;
using UiharuMind.Features.Clipboard;
using UiharuMind.Features.Conversation.SidePanels;
using UiharuMind.Features.QuickTools;
using UiharuMind.Features.Audio;

using UiharuMind.Shared.WindowManagement;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Shared.Shell;

public partial class MainViewModel : ViewModelBase //, IRecipient<string>
{
    private readonly IServiceProvider _services;

    // 显式工厂字典：替代 ActivatorUtilities 动态构造（AOT/裁剪友好）。
    // ViewModel 新增时在这里登记——这是 AOT 要的显式化，不是负担。
    // internal 供 App.Tests 断言集合完整性（新增忘登记时测试红）。
    internal static readonly Dictionary<Type, Func<IServiceProvider, ViewModelBase>> ViewModelFactories =
        new()
        {
            [typeof(QuickAutoClickViewModel)] = _ => new QuickAutoClickViewModel(),
            [typeof(ChatInfoModel)] = _ => new ChatInfoModel(),
            [typeof(ClipboardHistoryViewModel)] = _ => new ClipboardHistoryViewModel(),
            [typeof(LogViewModel)] = _ => new LogViewModel(),
            [typeof(SearchViewModel)] = sp => new SearchViewModel(sp.GetRequiredService<SearchService>()),
            [typeof(RadialMenuModel)] = _ => new RadialMenuModel(),
            [typeof(SettingViewModel)] = _ => new SettingViewModel(),
            [typeof(GeneralSettingViewModel)] = _ => new GeneralSettingViewModel(),
            [typeof(ShortcutSettingViewModel)] = _ => new ShortcutSettingViewModel(),
        };

    // 页面数据工厂：键即 MenuPages，值按 ActivatorUtilities 实际解析的构造签名从 sp 显式取参。
    // 未知键回落 MenuModelKey（与原 switch 的 default 分支一致）。
    internal static readonly Dictionary<MenuPages, Func<IServiceProvider, PageDataBase>> PageFactories =
        new()
        {
            [MenuPages.MenuConversationKey] = _ => new ConversationPageData(),
            [MenuPages.MenuCharacterKey] = sp => new HomePageData(sp.GetRequiredService<IMessageService>()),
            [MenuPages.MenuTranslateKey] = _ => new TranslatePageData(),
            [MenuPages.MenuModelKey] = sp => new ModelPageData(sp.GetRequiredService<IMessageService>(),
                audio: sp.GetRequiredService<AudioServices>()),
            [MenuPages.MenuServicesKey] = sp => new ServicesPageData(sp.GetRequiredService<IMessageService>()),
            [MenuPages.MenuLogKey] = _ => new LogPageData(),
            [MenuPages.MenuHelpKey] = _ => new HelpPageData(),
        };

    public MenuViewData Menus { get; set; } = new MenuViewData();

    // public Footer Footers { get; set; } = new Footer();
    public string Version => App.Version.ToString();

    [ObservableProperty] private bool _isMenuVisible = true;

    [ObservableProperty] private ViewModelBase? _content;

    private readonly Dictionary<MenuPages, PageDataBase> _viewPageModels = new Dictionary<MenuPages, PageDataBase>();
    private readonly Dictionary<Type, ViewModelBase> _viewModels = new Dictionary<Type, ViewModelBase>();

    public MainViewModel() : this(App.Services)
    {
    }

    public MainViewModel(IServiceProvider services)
    {
        _services = services;
        foreach (IEngineDistribution engine in LocalEngines.All)
            engine.AvailableUpdateChanged += () =>
                Dispatcher.UIThread.Post(() => OnPropertyChanged(nameof(HasEngineUpdate)));
        // 默认页排在空闲时才落：这之前已经有人跳过页（开发脚本第一步、托盘点进某页）就不盖掉它
        Dispatcher.UIThread.Post(() =>
        {
            if (Content == null) JumpToPage(MenuPages.MenuAgentKey);
        }, DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// 有本地引擎出了新版本：设置入口上挂个点
    /// </summary>
    public bool HasEngineUpdate => LocalEngines.All.Any(x => x.AvailableUpdate != null);

    [RelayCommand]
    private void OpenSetting()
    {
        UIManager.ShowWindow<SettingsWindow>();
    }

    [RelayCommand]
    private void ChangeMenuVisible()
    {
        IsMenuVisible = !IsMenuVisible;
    }

    partial void OnContentChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        PageSwitchPerfProbe.BeginSwitch(newValue?.GetType().Name ?? "none");
        oldValue?.OnDisable();
        newValue?.OnEnable();
        PageSwitchPerfProbe.ReportEnabled();
    }

    // public void Receive(string message)
    // {
    //     // Menus.MenuItems[0].MenuHeader =  message;
    //     Content = GetPage(message);
    //     // foreach (var menu in Menus.MenuItems)
    //     // {
    //     //     menu.IsSelected = menu.Key == message;
    //     // }
    // }

    public PageDataBase GetPage(MenuPages message)
    {
        // 对话页已合并：旧 agent/chat 键直接转发到统一入口，不在自己名下缓存——
        // 否则同一个 ConversationPageData 实例会挂进三个键，将来改旧键映射容易踩"两键两实例"的坑
        if (message is MenuPages.MenuAgentKey or MenuPages.MenuChatKey)
            return GetPage(MenuPages.MenuConversationKey);

        _viewPageModels.TryGetValue(message, out var vmPage);
        if (vmPage == null)
        {
            long vmCtorBegin = global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.Begin();
            vmPage = PageFactories.TryGetValue(message, out var factory)
                ? factory(_services)
                : GetPage(MenuPages.MenuModelKey);
            global::UiharuMind.Core.Core.Diagnostics.StartupPhaseProbe.End($"page/vm-ctor:{message}", vmCtorBegin);
            _viewPageModels.Add(message, vmPage);
        }

        return vmPage;
    }

    /// <summary>
    /// 获取缓存的 ViewModel
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <returns></returns>
    public T GetViewModel<T>() where T : ViewModelBase
    {
        _viewModels.TryGetValue(typeof(T), out var vm);
        if (vm == null)
        {
            vm = ViewModelFactories[typeof(T)](_services);
            _viewModels.Add(typeof(T), vm);
        }

        return (T)vm;
    }

    public void JumpToPage(MenuPages page)
    {
        if (page == MenuPages.MenuSettingKey)
        {
            UIManager.ShowWindow<SettingsWindow>();
            return;
        }

        Content = GetPage(page);
        // 对话页已合并成单入口：agent/chat 旧键与 MenuConversationKey 都点亮同一个菜单项
        bool isConversation = page is MenuPages.MenuConversationKey or MenuPages.MenuAgentKey or MenuPages.MenuChatKey;
        foreach (var menu in Menus.MenuItems)
        {
            menu.IsSelected = isConversation ? menu.Key == MenuPages.MenuConversationKey : menu.Key == page;
        }
    }
}

public enum MenuPages
{
    MenuConversationKey,
    MenuCharacterKey,
    MenuAgentKey,
    MenuChatKey,
    MenuTranslateKey,
    MenuModelKey,
    MenuServicesKey,
    MenuLogKey,
    MenuSettingKey,
    MenuAboutKey,
    MenuHelpKey,
}