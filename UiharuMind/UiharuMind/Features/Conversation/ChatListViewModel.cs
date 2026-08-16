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
using Avalonia.Threading;
using UiharuMind.Core.AI.Character;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 对话列表视图模型
/// </summary>
public partial class ChatListViewModel : ViewModelBase
{
    public ObservableCollection<SessionListItem> ChatSessions { get; } = new();

    [ObservableProperty] private SessionListItem? _selectedSession;

    public event Action<SessionListItem?>? EventOnSelectedSessionChanged;

    public ChatListViewModel()
    {
        // 只读索引,本体按需加载(agent 会话的工具结果是全量持久化的,启动时全量反序列化会卡死);
        // 只列出聊天页那两档的会话 —— 索引是与智能体页共用的
        foreach (ChatSessionMeta meta in SessionManager.Instance.GetChatSessions())
        {
            ChatSessions.Add(CreateItem(new SessionListItem(meta)));
        }

        SessionManager.Instance.OnSessionAdded += OnChatSessionAdded;
        SessionManager.Instance.OnSessionRemoved += OnChatSessionRemoved;
        // 运行态变化可能来自后台线程,列表项是界面绑定的,必须回到 UI 线程再改
        SessionManager.Instance.Running.StateChanged += id =>
            Dispatcher.UIThread.Post(() => RefreshRunState(id));

        if (ChatSessions.Count == 0)
            SessionManager.Instance.StartNewSession(CharacterManager.Instance.GetCharacterData(""));
        else SelectedSession = ChatSessions[0];
    }

    private void OnChatSessionAdded(ChatSession obj)
    {
        // 智能体会话归智能体页,不进本列表
        if (!obj.CharacterData.Kind.IsChat()) return;

        ChatSessions.Insert(0, CreateItem(new SessionListItem(obj)));
        SelectedSession = ChatSessions[0];
    }

    private void OnChatSessionRemoved(ChatSession obj)
    {
        // 按标识比对:拿 ChatSession 比对会触发每个列表项去加载本体,懒加载就白做了
        ChatSessions.RemvoeItem(x => x.SessionId == obj.SessionId);
        SelectedSession = ChatSessions.Count > 0 ? ChatSessions[0] : null;
    }

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        EventOnSelectedSessionChanged?.Invoke(value);
    }

    private void RefreshRunState(string sessionId)
    {
        foreach (SessionListItem item in ChatSessions)
        {
            if (item.SessionId == sessionId) item.RefreshRunState();
        }
    }

    private SessionListItem CreateItem(SessionListItem item)
    {
        item.Mutated += OnItemMutated;
        return item;
    }

    private void OnItemMutated(SessionListItem item)
    {
        // 被展示中的会话在条目上被就地改写(改名/清空),重发选中事件让内容区重载
        if (item == SelectedSession) EventOnSelectedSessionChanged?.Invoke(item);
    }
}
