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

using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character;
using UiharuMind.Features.Characters;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 聊天页面壳:左侧会话列表 + 中间通用对话组件 + 右侧插件面板。
/// 会话内容由 ConversationViewModel + ConversationView 承载,与 agent 页同一套。
/// </summary>
public partial class ChatPageData : ConversationPageDataBase
{
    protected override Control CreateView => new ChatPage();

    public ChatListViewModel ChatListViewModel { get; }

    private readonly ChatInfoModel _chatInfoModel;

    public ChatPageData()
    {
        ChatListViewModel = App.ViewModel.GetViewModel<ChatListViewModel>();
        _chatInfoModel = App.ViewModel.GetViewModel<ChatInfoModel>();

        ChatListViewModel.EventOnSelectedSessionChanged += OnSelectedSessionChanged;
        ChatListViewModel.EventOnSessionMutated += OnSessionMutated;
        OnSelectedSessionChanged(ChatListViewModel.SelectedSession);
    }

    protected override ConversationViewModel CreateConversation()
    {
        return new ConversationViewModel
        {
            // 无会话时首轮发送以默认角色开聊(与列表为空时自动建会话的角色一致)
            NewSessionCharacterId = nameof(DefaultCharacter.Empty),
            InputPlaceholderKey = "ChatInputTips",
        };
    }

    protected override void OnConversationCreated(ConversationViewModel conversation)
    {
        conversation.PropertyChanged += OnConversationPropertyChanged;
    }

    protected override void OnConversationDiscarding(ConversationViewModel conversation)
    {
        conversation.PropertyChanged -= OnConversationPropertyChanged;
    }

    private void OnSelectedSessionChanged(SessionListItem? obj)
    {
        SwitchConversation(obj?.Meta);
        _chatInfoModel.SetSession(obj);
        // 换实例后插件面板要按新实例的状态重新对齐(切到一个后台跑着的会话时它就是"进行中")
        NotifyChatState(Conversation.IsGenerating);
    }

    private void OnSessionMutated(SessionListItem item)
    {
        // 改名允许在跑的过程中进行,而重载会把界面条目清掉重新回放——正在流的那一轮会被拦腰截断
        if (FindConversation(item.Meta.SessionId) is not { IsGenerating: false } target) return;
        if (target == Conversation) _ = target.LoadSessionAsync(item.Meta);
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConversationViewModel.IsGenerating)) return;
        // 后台会话的起止不该扰动插件面板,它讲的是"你正在看的这个会话"
        if (!ReferenceEquals(sender, Conversation)) return;
        NotifyChatState(Conversation.IsGenerating);
    }

    private void NotifyChatState(bool isGenerating)
    {
        if (isGenerating) _chatInfoModel.NotifyChatBegin();
        else _chatInfoModel.NotifyChatEnd();
    }
}
