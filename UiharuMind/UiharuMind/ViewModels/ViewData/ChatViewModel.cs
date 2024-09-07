using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatViewModel : ViewModelBase
{
    public enum SendMode
    {
        User,
        Assistant
    }

    //发送模式，用户/助手
    [ObservableProperty] private SendMode _senderMode = SendMode.User;
    [ObservableProperty] private string _titleName = "";
    [ObservableProperty] private string _inputText = "";
    [ObservableProperty] private ChatSession _chatSession;

    public ObservableCollection<ChatViewItemData> ChatItems { get; } = new();

    [RelayCommand]
    public void ChangeSendMode()
    {
        SenderMode = SenderMode == SendMode.User ? SendMode.Assistant : SendMode.User;
        Log.Debug("ChangeSendModeCommand:" + SenderMode);
    }

    [RelayCommand]
    public void SendMessage()
    {
        Log.Debug("SendMessageCommand:" + InputText);
        AddMessage(InputText);
        InputText = "";
        SaveUtility.Save("chat_history.json", _chatSession);
    }
    //
    // [RelayCommand]
    // public void EnterKey()
    // {
    //     SendMessage();
    // }

    private void AddMessage(string message)
    {
        ChatSession.AddMessage(SenderMode == SendMode.User ? AuthorRole.User : AuthorRole.Assistant, message);
        AddMessage(ChatSession[^1]);
    }

    private void AddMessage(ChatMessage chatItem)
    {
        var chatViewItemData = SimpleObjectPool<ChatViewItemData>.Get();
        chatViewItemData.SetChatItem(chatItem);
        ChatItems.Add(chatViewItemData);
    }

    partial void OnChatSessionChanged(ChatSession? value)
    {
        foreach (var item in ChatItems)
        {
            SimpleObjectPool<ChatViewItemData>.Release(item);
        }

        ChatItems.Clear();

        if (value == null)
        {
            return;
        }

        foreach (var item in value)
        {
            AddMessage(item);
        }
    }
}