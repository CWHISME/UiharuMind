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
using UiharuMind.Core.AI.Chat;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Utils;

namespace UiharuMind.ViewModels.ViewData;

/// <summary>
/// 对话列表视图模型
/// </summary>
public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<ChatSessionViewData> ChatSessions { get; } = new();

    [ObservableProperty] private ChatSessionViewData? _selectedSession;

    public event Action<ChatSessionViewData?>? EventOnSelectedSessionChanged;

    public ChatListViewModel()
    {
        // 只读索引,本体按需加载(agent 会话的工具结果是全量持久化的,启动时全量反序列化会卡死);
        // 只列出对话角色的会话 —— 索引是与 agent 页共用的
        foreach (ChatSessionMeta meta in SessionManager.Instance.GetSessions(ECharacterKind.Roleplay))
        {
            ChatSessions.Add(new ChatSessionViewData(meta));
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

        ChatSessions.Insert(0, new ChatSessionViewData(obj));
        SelectedSession = ChatSessions[0];
    }

    private void OnChatSessionRemoved(ChatSession obj)
    {
        // 按标识比对:拿 ChatSession 比对会触发每个列表项去加载本体,懒加载就白做了
        ChatSessions.RemvoeItem(x => x.SessionId == obj.SessionId);
        SelectedSession = ChatSessions.Count > 0 ? ChatSessions[0] : null;
    }

    partial void OnSelectedSessionChanged(ChatSessionViewData? value)
    {
        value?.Active();
        EventOnSelectedSessionChanged?.Invoke(value);
    }
}