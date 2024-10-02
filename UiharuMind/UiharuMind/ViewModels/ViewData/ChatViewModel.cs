using System.Threading;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using SharpHook.Native;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 用于显示聊天信息，并提供输入框，用于发送消息
/// </summary>
public partial class ChatViewModel : ObservableObject
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
    [ObservableProperty] private KeyGesture _sendGesture = new KeyGesture(Key.Enter);
    [ObservableProperty] private bool _scrollToEnd;

    [ObservableProperty] private ChatSessionViewData _chatSession;

    //处于生成状态
    [ObservableProperty] private bool _isGenerating;

    private CancellationTokenSource? _cancelTokenSource;

    [RelayCommand]
    public void ChangeSendMode()
    {
        SenderMode = SenderMode == SendMode.User ? SendMode.Assistant : SendMode.User;
        Log.Debug("ChangeSendModeCommand:" + SenderMode);
    }

    [RelayCommand]
    public void SendMessage()
    {
        // if (!InputManager.Instance.IsPressed(KeyCode.VcLeftControl)) return;
        if (string.IsNullOrEmpty(InputText)) return;
        Log.Debug("SendMessageCommand:" + InputText);
        AddMessage(InputText);
        InputText = "";
        ScrollToEnd = true;
        SaveUtility.Save("chat_history.json", ChatSession);
        // Lang.Culture = CultureInfo.GetCultureInfo("mmm");
    }

    [RelayCommand]
    public void StopSending()
    {
        _cancelTokenSource.SafeStop();
        _cancelTokenSource = null;
    }

    private async void AddMessage(string message)
    {
        IsGenerating = true;
        _cancelTokenSource = new CancellationTokenSource();
        await ChatSession.AddMessageWithGenerate(SenderMode == SendMode.User ? AuthorRole.User : AuthorRole.Assistant,
            message, _cancelTokenSource.Token);
        IsGenerating = false;
    }
}