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
using UiharuMind.Services;
using UiharuMind.ViewModels.Pages;
using UiharuMind.Views;
using UiharuMind.Views.Controls;
using UiharuMind.Views.Windows;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase //, IRecipient<string>
{
    private readonly IServiceProvider _services;
    public MenuViewData Menus { get; set; } = new MenuViewData();

    public Footer Footers { get; set; } = new Footer();

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
            JumpToPage(MenuPages.MenuChatKey);
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
        oldValue?.OnDisable();
        newValue?.OnEnable();
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
        _viewPageModels.TryGetValue(message, out var vmPage);
        if (vmPage == null)
        {
            vmPage = message switch
            {
                MenuPages.MenuMainKey => ActivatorUtilities.CreateInstance<HomePageData>(_services),
                MenuPages.MenuChatKey => ActivatorUtilities.CreateInstance<ChatPageData>(_services),
                MenuPages.MenuTranslateKey => ActivatorUtilities.CreateInstance<TranslatePageData>(_services),
                MenuPages.MenuModelKey => ActivatorUtilities.CreateInstance<ModelPageData>(_services),
                MenuPages.MenuLogKey => ActivatorUtilities.CreateInstance<LogPageData>(_services),
                MenuPages.MenuHelpKey => ActivatorUtilities.CreateInstance<HelpPageData>(_services),
                _ => GetPage(MenuPages.MenuModelKey),
            };
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
        foreach (var menu in Menus.MenuItems)
        {
            menu.IsSelected = menu.Key == page;
        }
    }
}

public enum MenuPages
{
    MenuMainKey,
    MenuChatKey,
    MenuTranslateKey,
    MenuModelKey,
    MenuLogKey,
    MenuSettingKey,
    MenuAboutKey,
    MenuHelpKey,
}
