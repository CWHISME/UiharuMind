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

using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views;
using UiharuMind.Views.Pages;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.ViewModels.Pages;

public partial class ChatPageData : PageDataBase
{
    protected override Control CreateView => new ChatPage();

    private ChatViewModel ChatViewModel { get; set; }
    private ChatListViewModel ChatListViewModel { get; set; }

    [ObservableProperty] private float _leftPaneWidth = 250;
    [ObservableProperty] private float _rightPaneWidth = 200;

    // private List<ChatSession> _chatSessions = new List<ChatSession>();

    public ChatPageData()
    {
        // ChatViewModel = new ChatViewModel();
        // // _chatSessions.Add(new ChatSession());
        ChatViewModel = App.ViewModel.GetViewModel<ChatViewModel>();
        ChatListViewModel = App.ViewModel.GetViewModel<ChatListViewModel>();
        ChatListViewModel.EventOnSelectedSessionChanged += OnSelectedSessionChanged;
        ChatViewModel.ChatSession = ChatListViewModel.SelectedSession;
        // ChatViewModel.ChatSession = _chatSessions[0];
    }

    private void OnSelectedSessionChanged(ChatSessionViewData? obj)
    {
        ChatViewModel.ChatSession = obj;
    }

    // [RelayCommand]
    // public void ClickModelSelectCommand()
    // {
    // }
    //
    // [RelayCommand]
    // public void EjectModelCommand()
    // {
    // }

    [RelayCommand]
    public async Task AddChat()
    {
        var item = await CharacterSelectWindow.Show(UIManager.GetFoucusWindow());
        item?.StartChat();
    }
}