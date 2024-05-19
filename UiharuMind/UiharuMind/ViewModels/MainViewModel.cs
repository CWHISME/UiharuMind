using CommunityToolkit.Mvvm.Messaging;

namespace UiharuMind.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    public MenuViewData Menus { get; set; } = new MenuViewData();

    private object? _content;

    public object? Content
    {
        get => _content;
        set => SetProperty(ref _content, value);
    }

    public MainViewModel()
    {
        WeakReferenceMessenger.Default.Register<MainViewModel, string>(this, OnNavigation);
    }


    private void OnNavigation(MainViewModel vm, string s)
    {
        Content = s switch
        {
            MenuKeys.MenuMainPage => new MainPage(s),
            _ => new MainPage(s + "   Null Page"),
        };
    }
}
