using System.Collections.Generic;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core.Chat;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Pages;

namespace UiharuMind.ViewModels.Pages;

public partial class ChatPageData : PageDataBase
{
    protected override Control CreateView => new ChatPage();

    public ChatViewModel ChatViewModel { get; private set; }

    private List<ChatSession> _chatSessions = new List<ChatSession>();

    public ChatPageData()
    {
        ChatViewModel = new ChatViewModel();
        _chatSessions.Add(new ChatSession());
        ChatViewModel.ChatSession = _chatSessions[0];
    }

    [RelayCommand]
    public void ClickModelSelectCommand()
    {
    }

    [RelayCommand]
    public void EjectModelCommand()
    {
    }
}