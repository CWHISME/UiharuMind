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

using System;
using System.Threading;
using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using SharpHook.Native;
using UiharuMind.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Views;

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

    // [ObservableProperty] private string _inputToken = "";
    [ObservableProperty] private KeyGesture _sendGesture = new KeyGesture(Key.Enter);
    [ObservableProperty] private bool _scrollToEnd;

    [ObservableProperty] private ChatSessionViewData _chatSession;

    //处于生成状态
    [ObservableProperty] private bool _isGenerating;

    //显示纯文本选项
    [ObservableProperty] private bool _isPlaintext;

    //分页后的当前页索引
    [ObservableProperty] private int _currentPageIndex;

    //分页后的页面总数量
    [ObservableProperty] private int _currentItemCount;

    //是否显示分页，当超出一页之后才显示
    [ObservableProperty] private bool _isVisiblePagination;

    private CancellationTokenSource? _cancelTokenSource;

    public event Action<ChatSessionViewData>? OnEventChatSessionChanged;

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
        if (ChatSession == null)
        {
            App.MessageService.ShowWarningMessageBox("请先选择会话!", UIManager.GetFoucusWindow());
            return;
        }

        if (string.IsNullOrEmpty(InputText) || IsGenerating)
        {
            App.MessageService.ShowWarningMessageBox("请输入内容！", UIManager.GetFoucusWindow());
            return;
        }

        if (IsGenerating)
        {
            App.MessageService.ShowWarningMessageBox("正在生成中！", UIManager.GetFoucusWindow());
            return;
        }

        if (ChatSession.ChatSession.History.Count > 0 && ChatSession.ChatSession.History[^1].Role == AuthorRole.User)
        {
            GenerateMessage();
            return;
        }

        // Log.Debug("SendMessageCommand:" + InputText);
        AddMessage(InputText);
        InputText = "";
        ScrollToEnd = true;
        // SaveUtility.SaveRootFile("chat_history.json", ChatSession);
        // Lang.Culture = CultureInfo.GetCultureInfo("mmm");
    }

    [RelayCommand]
    public void StopSending(ChatSessionViewData chatSession)
    {
        _cancelTokenSource.SafeStop();
        _cancelTokenSource = null;
    }

    // [RelayCommand]
    // public void EditMessage(ChatViewItemData itemData)
    // {
    //     Log.Debug("EditCommand" + itemData.Message);
    // }
    //
    // [RelayCommand]
    // public void DeleteMessage(ChatViewItemData itemData)
    // {
    //     Log.Debug("DeleteCommand" + itemData);
    //     // ChatSession?.RemoveChatItem(itemData);
    // }

    private async void AddMessage(string message)
    {
        IsGenerating = true;
        _cancelTokenSource = new CancellationTokenSource();
        await ChatSession.AddMessageWithGenerate(SenderMode == SendMode.User ? AuthorRole.User : AuthorRole.Assistant,
            message, _cancelTokenSource.Token);
        IsGenerating = false;
    }

    private async void GenerateMessage()
    {
        IsGenerating = true;
        _cancelTokenSource = new CancellationTokenSource();
        await ChatSession.GenerateMessage(_cancelTokenSource.Token);
        IsGenerating = false;
    }

    partial void OnIsGeneratingChanged(bool value)
    {
        ConfigManager.Instance.Setting.IsChatPlainText = value;
    }

    partial void OnCurrentPageIndexChanged(int value)
    {
    }

    // partial void OnInputTextChanged(string value)
    // {
    //     if (string.IsNullOrEmpty(value))
    //     {
    //         InputToken = "";
    //         return;
    //     }
    //
    //     InputToken = $"Token: {LlmTokenizer.GetInputTokenCount(value)}";
    // }

    partial void OnChatSessionChanged(ChatSessionViewData value)
    {
        OnEventChatSessionChanged?.Invoke(value);
    }
}