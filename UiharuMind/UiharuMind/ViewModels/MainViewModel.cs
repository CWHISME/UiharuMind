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
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UiharuMind.Services;
using UiharuMind.ViewModels.Pages;
using UiharuMind.Views.Controls;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase //, IRecipient<string>
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    public Footer Footers { get; set; } = new Footer();

    [ObservableProperty] private bool _isMenuVisible = true;

    [ObservableProperty] private ViewModelBase? _content;

    public MessageService MessageService => App.MessageService;

    private readonly Dictionary<MenuPages, PageDataBase> _viewPageModels = new Dictionary<MenuPages, PageDataBase>();
    private readonly Dictionary<Type, ViewModelBase> _viewModels = new Dictionary<Type, ViewModelBase>();

    public MainViewModel()
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Receive(MenuKeys.MenuMainKey);
            // OnPropertyChanged();
            JumpToPage(MenuPages.MenuMainKey);
        }, DispatcherPriority.ApplicationIdle);
    }

    [RelayCommand]
    private async Task OpenSetting()
    {
        // Receive(MenuKeys.MenuSettingKey);
        await MessageService.ShowPageDrawer(GetPage(MenuPages.MenuAboutKey));
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
                MenuPages.MenuMainKey => new HomePageData(),
                MenuPages.MenuChatKey => new ChatPageData(),
                MenuPages.MenuTranslateKey => new TranslatePageData(),
                MenuPages.MenuModelKey => new ModelPageData(),
                MenuPages.MenuLogKey => new LogPageData(),
                MenuPages.MenuSettingKey => new SettingPageData(),
                MenuPages.MenuAboutKey => new AboutPageData(),
                _ => new ModelPageData() { Title = message + "   Null Page" },
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
    public T GetViewModel<T>() where T : ViewModelBase, new()
    {
        _viewModels.TryGetValue(typeof(T), out var vm);
        if (vm == null)
        {
            vm = new T();
            _viewModels.Add(typeof(T), vm);
        }

        return (T)vm;
    }

    public void JumpToPage(MenuPages page)
    {
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
    MenuAboutKey
}