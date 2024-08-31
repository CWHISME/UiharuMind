using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SharpHook.Native;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.ViewModels.Controls;
using UiharuMind.ViewModels.Pages;
using UiharuMind.ViewModels.ScreenCaptures;
using UiharuMind.Views.Capture;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase, IRecipient<string>
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    public Footer Footers { get; set; } = new Footer();

    [ObservableProperty] private bool _isMenuVisible = true;

    [ObservableProperty] private ViewModelBase? _content;

    private readonly Dictionary<string, ViewModelBase> _viewModels = new Dictionary<string, ViewModelBase>();

    public MainViewModel()
    {
        Receive(MenuKeys.MenuMainKey);
        Menus.MenuItems[0].IsSelected = true;
    }

    [RelayCommand]
    private void OpenSetting()
    {
        Receive(MenuKeys.MenuSettingKey);
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

    public ViewModelBase GetPage(string message)
    {
        _viewModels.TryGetValue(message, out var vmPage);
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
                _ => new ModelPageData() { Title = message + "   Null Page" },
            };
            _viewModels.Add(message, vmPage);
        }

        return vmPage;
    }
}