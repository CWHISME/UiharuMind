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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using UiharuMind.Services;
using UiharuMind.ViewModels.Pages;
using UiharuMind.Views.Controls;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase, IRecipient<string>
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    public Footer Footers { get; set; } = new Footer();

    [ObservableProperty] private bool _isMenuVisible = true;

    [ObservableProperty] private ViewModelBase? _content;

    public MessageService MessageService => App.MessageService;

    private readonly Dictionary<string, PageDataBase> _viewPageModels = new Dictionary<string, PageDataBase>();
    private readonly Dictionary<Type, ObservableObject> _viewModels = new Dictionary<Type, ObservableObject>();

    public MainViewModel()
    {
        Receive(MenuKeys.MenuMainKey);
        Menus.MenuItems[0].IsSelected = true;
        OnPropertyChanged();
    }

    [RelayCommand]
    private async Task OpenSetting()
    {
        // Receive(MenuKeys.MenuSettingKey);
        await MessageService.ShowPageDrawer(GetPage(MenuKeys.MenuAboutKey));
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

    public void Receive(string message)
    {
        // Menus.MenuItems[0].MenuHeader =  message;
        Content = GetPage(message);
    }

    public PageDataBase GetPage(string message)
    {
        _viewPageModels.TryGetValue(message, out var vmPage);
        if (vmPage == null)
        {
            vmPage = message switch
            {
                MenuKeys.MenuMainKey => new HomePageData(),
                MenuKeys.MenuChatKey => new ChatPageData(),
                MenuKeys.MenuTranslateKey => new TranslatePageData(),
                MenuKeys.MenuModelKey => new ModelPageData() { Title = message },
                MenuKeys.MenuLogKey => new LogPageData(),
                MenuKeys.MenuSettingKey => new SettingPageData(),
                MenuKeys.MenuAboutKey => new AboutPageData(),
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
    public T GetViewModel<T>() where T : ObservableObject, new()
    {
        _viewModels.TryGetValue(typeof(T), out var vm);
        if (vm == null)
        {
            vm = new T();
            _viewModels.Add(typeof(T), vm);
        }

        return (T)vm;
    }
}