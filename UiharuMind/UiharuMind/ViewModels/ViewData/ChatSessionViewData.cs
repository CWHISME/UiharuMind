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
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Utils;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 表示一个对话
/// </summary>
public partial class ChatSessionViewData : ObservableObject
{
    public readonly ChatSession ChatSession;

    [ObservableProperty] private string _name;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    [ObservableProperty] private string _memoryTipsName;
    [ObservableProperty] private MemoryData? _memoryData;

    // public ECharacter Character => _chatSession.CharaterId == 0 ? ECharacter.User : ECharacter.Assistant;
    // //如果是当天，返回具体时间，否则返回日期
    // public string Time => _chatSession.LastTime - DateTime.UtcNow.Ticks < TimeSpan.FromDays(1).Ticks
    //     ? _chatSession.LastTime.ToString("HH:mm")
    //     : _chatSession.LastTime.ToString("yyyy-MM-dd");

    /// <summary>
    /// 是否携带对话历史，仅工具人有效
    /// </summary>
    public bool IsNotTakeHistoryContext
    {
        get => ChatSession.IsNotTakeHistoryContext;
        set
        {
            ChatSession.IsNotTakeHistoryContext = value;
            OnPropertyChanged();
        }
    }

    public ObservableCollection<ChatViewItemData> ChatItems { get; } = new();
    public ChatViewItemData? CurrentChatItem { get; set; }

    /// <summary>
    /// 激活该对话
    /// </summary>
    public void Active()
    {
        SyncSession(ChatSession);
    }

    public ChatSessionViewData(ChatSession chatSession)
    {
        ChatSession = chatSession;
        Description = ChatSession.Description;
        Name = ChatSession.Name;
        Icon = IconUtils.GetCharacterBitmapOrDefault(ChatSession.CharacterData);
        TimeString = CalcTimeString();
        MemoryData = ChatSession.Memery;
        RefreshMemoryName();
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
        if (role != AuthorRole.User) return;
        await GenerateMessage(token);
    }

    /// <summary>
    /// 如果最后一条为 Assistant 则移除最后一条，并重新生成回复
    /// </summary>
    /// <param name="token"></param>
    public async Task GenerateMessage(CancellationToken token)
    {
        var lastMessage = ChatSession[^1];
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
        if (ChatItems.Count != ChatSession.Count)
        {
            //不同步，说明出问题了，强行重载
            SyncSession(ChatSession);
            Log.Warning("SyncSession(Different count): " + Name);
        }

        // else //(ChatItems.Count == _chatSession.Count)
        // && string.IsNullOrEmpty(ChatItems[^1].CachedContent?.Content))
        // {
        //与逻辑层一致，没问题，添加占位，先添加表现层的空消息
        CurrentChatItem = AddMessage(ChatSession.CreateMessage(AuthorRole.Assistant, ""));
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
            await foreach (var item in ChatSession.GenerateCompletionStreaming(token))
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
        catch (TaskCanceledException)
        {
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }


        // if (ChatItems.Count == _chatSession.Count && CurrentChatItem != null)
        //     CurrentChatItem.SetChatItem(_chatSession[^1]);
        // Log.Debug("GenerateMessage end："+CurrentChatItem?.Message);
        TimeString = CalcTimeString();
        // _chatSession[^1].Message.Content = CurrentChatItem?.Message;
    }

    [RelayCommand]
    //清除所有历史记录
    public void ClearChatHistory()
    {
        App.MessageService.ShowConfirmMessageBox(Lang.ClearTips,
            () =>
            {
                ChatSession.Clear();
                ChatItems.Clear();
                TimeString = "";
            });
    }

    [RelayCommand]
    //编辑
    public void EditCharater()
    {
        UIManager.ShowEditCharacterWindow(new CharacterInfoViewData(ChatSession.CharacterData),
            x => x.SaveCharacter());
    }

    [RelayCommand]
    //重命名
    public async Task Rename()
    {
        var result = await UIManager.ShowStringEditWindow(ChatSession.Name);
        if (!string.IsNullOrEmpty(result)) ModifySessionName(result);
    }

    [RelayCommand]
    //复制整个对话
    public void Copy()
    {
        ChatManager.Instance.Copy(ChatSession);
    }

    [RelayCommand]
    //删除整个对话
    public void Delete()
    {
        App.MessageService.ShowConfirmMessageBox(Lang.DeleteAllClipboardHistoryTips,
            () => { ChatManager.Instance.Delete(ChatSession); });
    }

    /// <summary>
    /// 删除指定消息
    /// </summary>
    /// <param name="itemData"></param>
    public void DeleteChatItem(ChatViewItemData itemData)
    {
        App.MessageService.ShowConfirmMessageBox(Lang.DeleteTips,
            () =>
            {
                int index = ChatItems.IndexOf(itemData);
                if (index < 0) return;
                ChatItems.RemoveAt(index);
                ChatSession.RemoveMessageAt(index);
            });
    }

    public void ModifySessionName(string newName)
    {
        Name = ChatManager.Instance.ModifyName(ChatSession, newName);
    }

    // public void ModifySessionDescription(string newName)
    // {
    //     Description = ChatManager.Instance.ModifySessionDescription(ChatSession, newName);
    // }

    public ChatViewItemData AddMessage(AuthorRole role, string message, byte[]? imageBytes = null)
    {
        ChatSession.AddMessage(role, message, imageBytes);
        return AddMessage(ChatSession[^1]);
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
        chatViewItemData.Icon = chatViewItemData.IsUser
            ? IconUtils.DefaultUserIcon
            : IconUtils.GetCharacterBitmapOrDefault(ChatSession.CharacterData);
        chatViewItemData.DeleteCallback = DeleteChatItem;
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
        // foreach (var item in ChatItems)
        // {
        //     SimpleObjectPool<ChatViewItemData>.Release(item);
        // }

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
        DateTime lastChatDate = ChatSession.LastTime.Date;

        if (currentDate == lastChatDate) return ChatSession.LastTime.ToString("HH:mm");
        return ChatSession.LastTime.ToString("yyyy/MM/dd");
    }

    partial void OnMemoryDataChanged(MemoryData? value)
    {
        ChatSession.Memery = value;
        RefreshMemoryName();
    }

    private void RefreshMemoryName()
    {
        MemoryTipsName = Lang.MemoryTitle + (MemoryData?.Name ?? Lang.NoMemory);
    }
}