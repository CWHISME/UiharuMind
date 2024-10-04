using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

public partial class ChatSessionViewData : ViewModelBase
{
    private readonly ChatSession _chatSession;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    public ECharacter Character => _chatSession.CharaterId == 0 ? ECharacter.User : ECharacter.Assistant;
    // //如果是当天，返回具体时间，否则返回日期
    // public string Time => _chatSession.LastTime - DateTime.UtcNow.Ticks < TimeSpan.FromDays(1).Ticks
    //     ? _chatSession.LastTime.ToString("HH:mm")
    //     : _chatSession.LastTime.ToString("yyyy-MM-dd");

    public ObservableCollection<ChatViewItemData> ChatItems { get; } = new();
    public ChatViewItemData? CurrentChatItem { get; set; }

    /// <summary>
    /// 激活该对话
    /// </summary>
    public void Active()
    {
        SyncSession(_chatSession);
    }

    public ChatSessionViewData(ChatSession chatSession)
    {
        _chatSession = chatSession;
        Description = _chatSession.Description;
        Name = _chatSession.Name;
        TimeString = CalcTimeString();
    }

    public async Task AddMessageWithGenerate(AuthorRole role, string message, CancellationToken token)
    {
        // _chatSession.AddMessage(role, message);
        // //添加用户消息
        // AddMessage(_chatSession[^1]);
        AddMessage(role, message);
        //生成AI回复
        CurrentChatItem = AddMessage(AuthorRole.Assistant, "");
        // _chatSession.GenerateCompletion(OnStartGenerate,OnStepGenerated,OnCompletionGenerated,new CancellationToken());
        await GenerateMessage(token);
    }

    public async Task GenerateMessage(CancellationToken token)
    {
        var lastMessage = _chatSession[^1];
        if (lastMessage.Character == ECharacter.User)
        {
            Log.Warning("Error: User cannot generate message");
            return;
        }

        var currentChatItem = CurrentChatItem;
        if (currentChatItem == null)
        {
            Log.Error("Error: CurrentChatItem is null");
            return;
        }

        _chatSession.GenerateCompletionStreaming(() => { currentChatItem.SetChatItem(_chatSession[^1]); }, (message) =>
            {
                currentChatItem.Message = message.Message;
                currentChatItem.TokenCount = message.TokenCount;
            },
            () => { currentChatItem.SetChatItem(_chatSession[^1]); }, token);

        // await foreach (var item in _chatSession.GenerateCompletionAsync(token))
        // {
        //     if (CurrentChatItem == null)
        //     {
        //         Log.Error("Error: CurrentChatItem is null");
        //         break;
        //     }
        //
        //     CurrentChatItem.Message = item;
        // }

        // _chatSession[^1].Message.Content = CurrentChatItem?.Message;
    }

    private ChatViewItemData AddMessage(AuthorRole role, string message)
    {
        _chatSession.AddMessage(role, message);
        return AddMessage(_chatSession[^1]);
    }

    private void OnStartGenerate(ChatMessage obj)
    {
        CurrentChatItem = AddMessage(obj);
    }

    private void OnStepGenerated(string obj)
    {
        if (CurrentChatItem == null)
        {
            Log.Warning("CurrentChatItem is null, step generated: " + obj);
            return;
        }

        CurrentChatItem.Message = obj;
    }

    private void OnCompletionGenerated(string obj)
    {
    }

    private ChatViewItemData AddMessage(ChatMessage chatItem)
    {
        var chatViewItemData = SimpleObjectPool<ChatViewItemData>.Get();
        chatViewItemData.SetChatItem(chatItem);
        ChatItems.Add(chatViewItemData);
        TimeString = CalcTimeString();
        return chatViewItemData;
    }

    /// <summary>
    /// 将会话实际数据同步到视图
    /// </summary>
    /// <param name="value"></param>
    private void SyncSession(ChatSession? value)
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

    private string CalcTimeString()
    {
        DateTime currentDate = DateTime.Now.Date;
        DateTime lastChatDate = _chatSession.LastTime.Date;

        if (currentDate == lastChatDate) return _chatSession.LastTime.ToString("HH:mm");
        return _chatSession.LastTime.ToString("yyyy/MM/dd");
    }
}