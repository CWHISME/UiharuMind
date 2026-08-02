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
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Memory;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;
using UiharuMind.Utils;
using UiharuMind.Views;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 表示一个对话
/// </summary>
public partial class ChatSessionViewData : ObservableObject
{
    private readonly IMessageService _messageService;
    private readonly ChatSessionMeta _meta;
    private ChatSession? _session;

    /// <summary>
    /// 会话本体，首次访问时按需加载。
    /// 列表展示所需的字段全部取自元数据，因此启动时不必反序列化任何本体——
    /// agent 会话的工具结果是全量持久化的，全量加载会卡死。
    /// </summary>
    public ChatSession ChatSession => _session ??=
        SessionManager.Instance.Load(_meta.SessionId) ?? new ChatSession { SessionId = _meta.SessionId };

    [ObservableProperty] private string _name;
    [ObservableProperty] private Bitmap? _icon;
    [ObservableProperty] private string _description;
    [ObservableProperty] private string _timeString;

    [ObservableProperty] private string _memoryTipsName;

    [ObservableProperty] private MemoryData? _memoryData;

    [ObservableProperty] private string? _memoryIndexTips;
    [ObservableProperty] private string _memoryStatusKey = "None";

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
        SyncSession(ChatSession);
    }

    public ChatSessionViewData(ChatSession chatSession)
        : this(chatSession, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ChatSessionViewData(ChatSession chatSession, IMessageService messageService)
        : this(chatSession.ToMeta(), messageService)
    {
        _session = chatSession;
    }

    public ChatSessionViewData(ChatSessionMeta meta)
        : this(meta, App.Services.GetRequiredService<IMessageService>())
    {
    }

    public ChatSessionViewData(ChatSessionMeta meta, IMessageService messageService)
    {
        _messageService = messageService;
        _meta = meta;
        Description = meta.Description;
        Name = meta.Title;
        Icon = IconUtils.GetCharacterBitmapOrDefault(
            CharacterManager.Instance.GetCharacterData(meta.CharacterId));
        TimeString = CalcTimeString();
        MemoryData = ResolveMemory(meta);
        MemoryTipsName = "";
        RefreshMemoryInfo();
    }

    /// <summary>
    /// 解析记忆库而不触发本体加载
    /// </summary>
    private static MemoryData? ResolveMemory(ChatSessionMeta meta)
    {
        if (!string.IsNullOrEmpty(meta.MemoryName) &&
            MemoryManager.Instance.TryGetMemoryData(meta.MemoryName, out MemoryData? memory))
        {
            return memory;
        }

        return CharacterManager.Instance.GetCharacterData(meta.CharacterId).Memory;
    }

    /// <summary>
    /// 追加一条用户消息并生成回复
    /// </summary>
    /// <param name="role">角色</param>
    /// <param name="message">文本</param>
    /// <param name="token">取消令牌</param>
    public async Task AddMessageWithGenerate(ChatRole role, string message, CancellationToken token)
    {
        // 本轮输入不预先写入历史:历史由 SessionChatHistoryProvider 在轮次结束时
        // 连同回复一起写入,预先加会导致重复。界面条目照常立即显示。
        ChatMessage input = ChatSession.CreateMessage(role, message);
        AddMessage(input);

        if (role != ChatRole.User) return;
        await GenerateMessage(input, token);
    }

    /// <summary>
    /// 生成一条回复
    /// </summary>
    /// <param name="input">本轮用户输入；为 null 表示基于现有历史重新生成</param>
    /// <param name="token">取消令牌</param>
    public async Task GenerateMessage(ChatMessage? input, CancellationToken token)
    {
        if (input == null && ChatSession.Count > 0 && ChatSession[^1].Role == ChatRole.Assistant)
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

        // 本轮输入已在界面上但还未进历史,因此期望值要带上它
        int expectedItems = ChatSession.Count + (input != null ? 1 : 0);
        if (ChatItems.Count != expectedItems)
        {
            //不同步，说明出问题了，强行重载
            SyncSession(ChatSession);
            Log.Warning("SyncSession(Different count): " + Name);
            if (input != null) AddMessage(input);
        }
        
        //与逻辑层一致，没问题，添加占位，先添加表现层的空消息
        CurrentChatItem = AddMessage(ChatSession.CreateMessage(ChatRole.Assistant, ""));

        try
        {
            await foreach (var item in ChatSession.GenerateCompletionStreaming(input, token))
            {
                if (CurrentChatItem != null)
                {
                    //TODO:绑定方式全量更新 Markdown 比较费，需要优化
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
            Log.Error(e.ToString());
        }

        if (CurrentChatItem != null)
        {
            if (ChatSession.Count == ChatItems.Count &&
                ChatSession[^1].Role == ChatRole.Assistant)
            {
                CurrentChatItem.SetChatItem(ChatSession[^1]);
            }
            else if (string.IsNullOrEmpty(CurrentChatItem.Message))
            {
                ChatItems.Remove(CurrentChatItem);
            }

            CurrentChatItem.IsDone = true;
        }

        // if (ChatItems.Count == _chatSession.Count && CurrentChatItem != null)
        //     CurrentChatItem.SetChatItem(_chatSession[^1]);
        // Log.Debug("GenerateMessage end："+CurrentChatItem?.Message);
        TimeString = CalcTimeString();
        // _chatSession[^1].Message.Content = CurrentChatItem?.Message;
    }

    [RelayCommand]
    //清除所有历史记录
    public async Task ClearChatHistory()
    {
        if (!await _messageService.ConfirmAsync(Lang.ClearTips)) return;
        ChatSession.Clear();
        ChatItems.Clear();
        TimeString = "";
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
        var result = await UIManager.ShowStringEditWindow(ChatSession.Title);
        if (!string.IsNullOrEmpty(result)) ModifySessionName(result);
    }

    [RelayCommand]
    //复制整个对话
    public void Copy()
    {
        SessionManager.Instance.Copy(ChatSession);
    }

    public void BranchFromChatItem(ChatViewItemData itemData)
    {
        int index = ChatItems.IndexOf(itemData);
        if (index < 0) return;

        var branchSession = SessionManager.Instance.DeepCopy(ChatSession);
        var suffix = LocalizationManager.Instance.GetString("ChatBranchSuffix");
        branchSession.Title = $"{ChatSession.Title} {suffix}";
        branchSession.Description = ChatSession.Description;

        while (branchSession.Count > index + 1)
        {
            int lastIndex = branchSession.Count - 1;
            branchSession.History.RemoveAt(lastIndex);
        }

        SessionManager.Instance.Add(branchSession);
    }

    [RelayCommand]
    //删除整个对话
    public async Task Delete()
    {
        if (await _messageService.ConfirmAsync(Lang.DeleteAllClipboardHistoryTips))
            SessionManager.Instance.Delete(ChatSession);
    }

    /// <summary>
    /// 删除指定消息
    /// </summary>
    /// <param name="itemData"></param>
    public async void DeleteChatItem(ChatViewItemData itemData)
    {
        if (!await _messageService.ConfirmAsync(Lang.DeleteTips)) return;
        int index = ChatItems.IndexOf(itemData);
        if (index < 0) return;
        ChatItems.RemoveAt(index);
        ChatSession.RemoveMessageAt(index);
    }

    public void ModifySessionName(string newName)
    {
        // 标题是纯显示字段:改名不动文件、不删不加、不再触发列表的 移除+新增 事件
        ChatSession.Title = newName;
        ChatSession.Save();
        Name = newName;
    }

    public ChatViewItemData AddMessage(ChatRole role, string message, byte[]? imageBytes = null)
    {
        ChatSession.AddMessage(role, message, imageBytes);
        return AddMessage(ChatSession[^1]);
    }

    //===================================记忆=====================================

    [RelayCommand]
    private void MemoryEditor()
    {
        UIManager.ShowMemorySelectWindow(UIManager.GetFocusWindow(), x => { MemoryData = x; },
            MemoryData);
    }

    //============================================================

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

        foreach (var item in value.History)
        {
            AddMessage(item);
        }
    }

    private string CalcTimeString()
    {
        // 已加载则用末条消息时间,否则用元数据的更新时间——不为一行时间去加载本体
        DateTime lastTime = _session?.LastTime ?? _meta.UpdatedAt.LocalDateTime;
        return DateTime.Now.Date == lastTime.Date
            ? lastTime.ToString("HH:mm")
            : lastTime.ToString("yyyy/MM/dd");
    }

    partial void OnMemoryDataChanged(MemoryData? oldValue, MemoryData? newValue)
    {
        if (oldValue != null) oldValue.StateChanged -= OnMemoryStateChanged;
        if (newValue != null) newValue.StateChanged += OnMemoryStateChanged;
        ChatSession.Memory = newValue;
        RefreshMemoryInfo();
    }

    private void OnMemoryStateChanged()
    {
        Dispatcher.UIThread.Post(RefreshMemoryInfo);
    }

    private void RefreshMemoryInfo()
    {
        RefreshMemoryName();
        RefreshMemoryIndexTips();
    }

    private void RefreshMemoryName()
    {
        MemoryTipsName = Lang.MemoryTitle + (MemoryData?.Name ?? Lang.NoMemory);
    }

    private void RefreshMemoryIndexTips()
    {
        var memory = MemoryData;
        if (memory == null)
        {
            MemoryIndexTips = Lang.NoMemory;
            MemoryStatusKey = "None";
            return;
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine(GetMemoryIndexStateText(memory));
        if (memory.LastIndexedAt != null)
            sb.AppendLine(Lang.MemoryIndexLastIndexed + memory.LastIndexedAt.Value.ToLocalTime().ToString("yyyy/MM/dd HH:mm"));
        if (!string.IsNullOrEmpty(memory.LastIndexError))
            sb.AppendLine(Lang.MemoryIndexLastError + GetMemoryIndexErrorText(memory.LastIndexError));

        MemoryIndexTips = sb.ToString();
        MemoryStatusKey = !string.IsNullOrEmpty(memory.LastIndexError) ? "Error" :
            memory.IndexDirty || memory.LastIndexedAt == null ? "Dirty" : "Ready";
    }

    private string GetMemoryIndexStateText(MemoryData memory)
    {
        if (memory.IndexDirty) return Lang.MemoryIndexNeedUpdate;
        if (memory.LastIndexedAt == null) return Lang.MemoryIndexNotBuilt;
        return Lang.MemoryIndexReady;
    }

    private static string GetMemoryIndexErrorText(string error)
    {
        if (error.StartsWith("Embedding model startup failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Failed to load LLamaSharp embedding model", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Remote embedding backend is not implemented", StringComparison.OrdinalIgnoreCase))
            return Lang.MemoryIndexEmbeddingServerUnavailable;

        if (error.StartsWith("LLamaSharp embedding request failed", StringComparison.OrdinalIgnoreCase))
            return GetLocalizedText("MemoryIndexEmbeddingRequestFailed");

        return error switch
        {
            "Embedding server is unavailable." => Lang.MemoryIndexEmbeddingServerUnavailable,
            "Embedding model is unavailable." => Lang.MemoryIndexEmbeddingServerUnavailable,
            "Embedding server startup timed out." => Lang.MemoryIndexEmbeddingServerTimeout,
            "Memory name not set" => Lang.MemoryIndexMemoryNameMissing,
            "Memory vector store unavailable" => Lang.MemoryIndexVectorStoreUnavailable,
            "Memory index update failed" => Lang.MemoryIndexUpdateFailed,
            "Memory source validation failed" => GetLocalizedText("MemorySourceValidationFailed"),
            "Memory vector dimension mismatch" => GetLocalizedText("MemoryIndexDimensionMismatch"),
            "Embedding input is too large" => GetLocalizedText("MemoryIndexEmbeddingInputTooLarge"),
            _ => error
        };
    }

    private static string GetLocalizedText(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}
