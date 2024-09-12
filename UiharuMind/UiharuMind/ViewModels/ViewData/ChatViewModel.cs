using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 用于显示聊天信息，并提供输入框，用于发送消息
/// </summary>
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
    [ObservableProperty] private ChatSessionViewData _chatSession;


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
        SaveUtility.Save("chat_history.json", ChatSession);
        // Lang.Culture = CultureInfo.GetCultureInfo("mmm");
    }

    private void AddMessage(string message)
    {
        ChatSession.AddMessage(SenderMode == SendMode.User ? AuthorRole.User : AuthorRole.Assistant, message);
    }
}