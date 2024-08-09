using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class ChatPageData : PageDataBase
{
    protected override Control CreateView => new ChatPage();

    [RelayCommand]
    public void ClickModelSelectCommand()
    {
        
    }

    [RelayCommand]
    public void EjectModelCommand()
    {
        
    }
}