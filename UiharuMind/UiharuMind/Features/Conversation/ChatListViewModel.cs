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
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话列表视图模型
/// </summary>
public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<ChatSessionItemViewData> ChatSessions { get; } = new();

    [ObservableProperty] private ChatSessionItemViewData? _selectedSession;

    public event Action<ChatSessionItemViewData?>? EventOnSelectedSessionChanged;

    public ChatListViewModel()
    {
        // 只读索引,本体按需加载(agent 会话的工具结果是全量持久化的,启动时全量反序列化会卡死);
        // 只列出对话角色的会话 —— 索引是与 agent 页共用的
        foreach (ChatSessionMeta meta in SessionManager.Instance.GetSessions(ECharacterKind.Roleplay))
        {
            ChatSessions.Add(CreateItem(new ChatSessionItemViewData(meta)));
        }

        SessionManager.Instance.OnSessionAdded += OnChatSessionAdded;
        SessionManager.Instance.OnSessionRemoved += OnChatSessionRemoved;

        if (ChatSessions.Count == 0)
            SessionManager.Instance.StartNewSession(CharacterManager.Instance.GetCharacterData(""));
        else SelectedSession = ChatSessions[0];
    }

    private void OnChatSessionAdded(ChatSession obj)
    {
        // agent 会话归 agent 页,不进本列表
        if (obj.CharacterData.Kind != ECharacterKind.Roleplay) return;

        ChatSessions.Insert(0, CreateItem(new ChatSessionItemViewData(obj)));
        SelectedSession = ChatSessions[0];
    }

    private void OnChatSessionRemoved(ChatSession obj)
    {
        // 按标识比对:拿 ChatSession 比对会触发每个列表项去加载本体,懒加载就白做了
        ChatSessions.RemvoeItem(x => x.SessionId == obj.SessionId);
        SelectedSession = ChatSessions.Count > 0 ? ChatSessions[0] : null;
    }

    partial void OnSelectedSessionChanged(ChatSessionItemViewData? value)
    {
        EventOnSelectedSessionChanged?.Invoke(value);
    }

    private ChatSessionItemViewData CreateItem(ChatSessionItemViewData item)
    {
        item.OnSessionMutated += OnItemMutated;
        return item;
    }

    private void OnItemMutated(ChatSessionItemViewData item)
    {
        // 被展示中的会话在条目上被就地改写(改名/清空),重发选中事件让内容区重载
        if (item == SelectedSession) EventOnSelectedSessionChanged?.Invoke(item);
    }
}
