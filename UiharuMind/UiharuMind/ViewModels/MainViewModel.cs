using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using UiharuMind.ViewModels.Pages;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase, IRecipient<string>
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    [ObservableProperty] private object? _content;

    private readonly Dictionary<string, ViewModelBase> _viewModels = new Dictionary<string, ViewModelBase>();

    // public MainViewModel()
    // {
    //     IsActive = true;
    // }

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

        Content = vmPage;
    }
}