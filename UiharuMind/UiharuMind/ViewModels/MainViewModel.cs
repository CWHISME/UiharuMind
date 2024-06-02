﻿using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using UiharuMind.ViewModels.Controls;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase, IRecipient<string>
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    public Footer Footers { get; set; } = new Footer();

    [ObservableProperty] private ViewModelBase? _content;

    private readonly Dictionary<string, ViewModelBase> _viewModels = new Dictionary<string, ViewModelBase>();

    public MainViewModel()
    {
        Receive(MenuKeys.MenuMainPage);
        Menus.MenuItems[0].IsSelected = true;
    }

    partial void OnContentChanged(ViewModelBase? oldValue, ViewModelBase? newValue)
    {
        oldValue?.OnDisable();
        newValue?.OnEnable();
    }

    public void Receive(string message)
    {
        _viewModels.TryGetValue(message, out var vmPage);
        if (vmPage == null)
        {
            vmPage = message switch
            {
                MenuKeys.MenuMainPage => new MainPageData() { Title = message },
                _ => new MainPageData() { Title = message + "   Null Page" },
            };
            _viewModels.Add(message, vmPage);
        }

        // Menus.MenuItems[0].MenuHeader =  message;
        Content = vmPage;
    }
}