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

using UiharuMind.Shared.WindowManagement;
namespace UiharuMind.Shared.Shell;

public partial class MainViewModel : ViewModelBase //, IRecipient<string>
{
    private readonly IServiceProvider _services;
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
        Dispatcher.UIThread.Post(() =>
        {
            // Receive(MenuKeys.MenuMainKey);
            // OnPropertyChanged();
            JumpToPage(MenuPages.MenuAgentKey);
        }, DispatcherPriority.ApplicationIdle);
    }

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
            vmPage = message switch
            {
                MenuPages.MenuConversationKey => ActivatorUtilities.CreateInstance<ConversationPageData>(_services),
                MenuPages.MenuCharacterKey => ActivatorUtilities.CreateInstance<HomePageData>(_services),
                MenuPages.MenuTranslateKey => ActivatorUtilities.CreateInstance<TranslatePageData>(_services),
                MenuPages.MenuModelKey => ActivatorUtilities.CreateInstance<ModelPageData>(_services),
                MenuPages.MenuServicesKey => ActivatorUtilities.CreateInstance<ServicesPageData>(_services),
                MenuPages.MenuLogKey => ActivatorUtilities.CreateInstance<LogPageData>(_services),
                MenuPages.MenuHelpKey => ActivatorUtilities.CreateInstance<HelpPageData>(_services),
                _ => GetPage(MenuPages.MenuModelKey),
            };
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
            vm = ActivatorUtilities.CreateInstance<T>(_services);
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