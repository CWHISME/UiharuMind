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
using Avalonia.Controls;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SessionList;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.Features.Conversation.Pages;

/// <summary>
/// 聊天页面壳:左侧会话列表 + 中间通用对话组件 + 右侧插件面板。
/// 会话内容由 ConversationViewModel + ConversationView 承载,与 agent 页同一套。
/// </summary>
public partial class ChatPageData : ConversationPageDataBase
{
    protected override Control CreateView => new ChatPage();

    private readonly ChatInfoModel _chatInfoModel;

    /// <summary>聊天页是<b>急建</b>——新会话自动选中，见 SessionListModel 的参数说明</summary>
    public ChatPageData() : base(ESessionListScope.Chat, selectNewSessions: true)
    {
        _chatInfoModel = App.ViewModel.GetViewModel<ChatInfoModel>();

        SessionList.SelectionChanged += OnSelectedSessionChanged;
        SessionList.Mutated += OnSessionMutated;
        // 删掉当前会话后顺位选下一条:聊天页的会话是急建的,列表不该空着
        SessionList.Removed += _ => SessionList.SelectFirstOrNone();

        SessionListItem? first = SessionList.Sessions.Count > 0 ? SessionList.Sessions[0] : null;
        OnSelectedSessionChanged(first);
        SessionList.SelectWithoutNotifying(first);

        // 一条会话都没有时先开一个:聊天页不提供"空态"的落点(新建按钮开的是角色选择器)。
        // 这一条会经 OnSessionAdded 进列表并自动选中
        if (first == null)
        {
            SessionManager.Instance.StartNewSession(CharacterManager.Instance.GetCharacterData(""));
        }
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
        // 换实例后详情栏要按新实例的状态重新对齐(切到一个后台跑着的会话时它就是"进行中")
        if (Conversation.IsGenerating) _chatInfoModel.NotifyChatBegin();
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
        // 后台会话的起止不该扰动详情栏,它讲的是"你正在看的这个会话"
        if (!ReferenceEquals(sender, Conversation)) return;
        // 只有"开始"有人关心(参数面板据此落盘),"结束"从来没有订阅方
        if (Conversation.IsGenerating) _chatInfoModel.NotifyChatBegin();
    }
}
