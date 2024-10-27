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
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 表示一个对话
/// </summary>
public partial class ChatSessionViewData : ObservableObject
{
    private readonly ChatSession _chatSession;

    [ObservableProperty] private string _name;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    // public ECharacter Character => _chatSession.CharaterId == 0 ? ECharacter.User : ECharacter.Assistant;
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
        //只有用户消息才生成AI回复
        // if (role != AuthorRole.User) return;
        //生成AI回复
        // _chatSession.GenerateCompletion(OnStartGenerate,OnStepGenerated,OnCompletionGenerated,new CancellationToken());
        await GenerateMessage(token);
    }

    /// <summary>
    /// 如果最后一条为 Assistant 则移除最后一条，并重新生成回复
    /// </summary>
    /// <param name="token"></param>
    public async Task GenerateMessage(CancellationToken token)
    {
        var lastMessage = _chatSession[^1];
        if (lastMessage.Character == ECharacter.Assistant)
        {
            Log.Error("Error: Assistant cannot generate message");
            return;
        }

        // var currentChatItem = CurrentChatItem;
        // if (currentChatItem == null)
        // {
        //     Log.Error("Error: CurrentChatItem is null");
        //     return;
        // }

        // ChatViewItemData currentChatItem = null;

        // if (Math.Abs(ChatItems.Count - _chatSession.Count) > 0)
        if (ChatItems.Count != _chatSession.Count)
        {
            //不同步，说明出问题了，强行重载
            SyncSession(_chatSession);
            Log.Warning("SyncSession(Different count): " + Name);
        }

        // else //(ChatItems.Count == _chatSession.Count)
        // && string.IsNullOrEmpty(ChatItems[^1].CachedContent?.Content))
        // {
        //与逻辑层一致，没问题，添加占位，先添加表现层的空消息
        CurrentChatItem = AddMessage(_chatSession.CreateMessage(AuthorRole.Assistant, ""));
        // }

        // //检测当前最后一条是否合法
        // if (lastMessage.Message != currentChatItem.CachedContent)
        // {
        // }

        //先添加表现层的空消息
        // CurrentChatItem = AddMessage(_chatSession.CreateMessage(AuthorRole.Assistant, ""));


        // await Task.Run(() =>
        // {
        //     bool isCompleted = false;
        //     _chatSession.GenerateCompletionStreaming(() =>
        //         {
        //             currentChatItem.IsDone = false;
        //             currentChatItem.SetChatItem(_chatSession[^1]);
        //         },
        //         (message) =>
        //         {
        //             currentChatItem.Message = message.Message;
        //             currentChatItem.TokenCount = message.TokenCount;
        //         },
        //         (message) =>
        //         {
        //             currentChatItem.Message = message.Message;
        //             currentChatItem.TokenCount = message.TokenCount;
        //             currentChatItem.IsDone = true;
        //             isCompleted = true;
        //         }, token);
        //     while (!isCompleted)
        //     {
        //         Thread.Sleep(1000);
        //     }
        // }, token);


        // await foreach (var item in LlmManager.Instance.CurrentRunningModel!.InvokeAgentStreamingAsync(_chatSession,
        //                    token))
        // {
        //     if (CurrentChatItem != null)
        //     {
        //         CurrentChatItem.Message = item;
        //     }
        // }

        try
        {
            await foreach (var item in _chatSession.GenerateCompletionStreaming(token))
            {
                if (CurrentChatItem != null)
                {
                    CurrentChatItem.Message = item;
                }
            }
        }
        catch (IOException)
        {
        }


        // if (ChatItems.Count == _chatSession.Count && CurrentChatItem != null)
        //     CurrentChatItem.SetChatItem(_chatSession[^1]);
        // Log.Debug("GenerateMessage end："+CurrentChatItem?.Message);
        TimeString = CalcTimeString();
        // _chatSession[^1].Message.Content = CurrentChatItem?.Message;
    }

    [RelayCommand]
    public void ClearChatHistory()
    {
        _chatSession.Clear();
        ChatItems.Clear();
        TimeString = "";
    }

    private ChatViewItemData AddMessage(AuthorRole role, string message)
    {
        _chatSession.AddMessage(role, message);
        return AddMessage(_chatSession[^1]);
    }

    // private void OnStartGenerate(ChatMessage obj)
    // {
    //     CurrentChatItem = AddMessage(obj);
    // }
    //
    // private void OnStepGenerated(string obj)
    // {
    //     if (CurrentChatItem == null)
    //     {
    //         Log.Warning("CurrentChatItem is null, step generated: " + obj);
    //         return;
    //     }
    //
    //     CurrentChatItem.Message = obj;
    // }

    // private void OnCompletionGenerated(string obj)
    // {
    // }

    private ChatViewItemData AddMessage(ChatMessage chatItem)
    {
        var chatViewItemData = new ChatViewItemData(); //SimpleObjectPool<ChatViewItemData>.Get();
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