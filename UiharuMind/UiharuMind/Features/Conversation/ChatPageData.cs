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

    /// <summary>会话内容视图模型(ConversationView 的 DataContext)</summary>
    public ConversationViewModel Conversation { get; } = new();

    public ChatListViewModel ChatListViewModel { get; }

    private readonly ChatInfoModel _chatInfoModel;

    public ChatPageData()
    {
        // 无会话时首轮发送以默认角色开聊(与列表为空时自动建会话的角色一致)
        Conversation.NewSessionCharacterId = nameof(DefaultCharacter.Empty);
        Conversation.InputPlaceholderKey = "ChatInputTips";

        ChatListViewModel = App.ViewModel.GetViewModel<ChatListViewModel>();
        _chatInfoModel = App.ViewModel.GetViewModel<ChatInfoModel>();

        ChatListViewModel.EventOnSelectedSessionChanged += OnSelectedSessionChanged;
        Conversation.PropertyChanged += OnConversationPropertyChanged;
        OnSelectedSessionChanged(ChatListViewModel.SelectedSession);
    }

    private void OnSelectedSessionChanged(SessionListItem? obj)
    {
        _ = Conversation.LoadSessionAsync(obj?.Meta);
        _chatInfoModel.SetSession(obj);
    }

    private void OnConversationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConversationViewModel.IsGenerating)) return;
        if (Conversation.IsGenerating) _chatInfoModel.NotifyChatBegin();
        else _chatInfoModel.NotifyChatEnd();
    }

    [RelayCommand]
    public async Task AddChat()
    {
        var item = await CharacterSelectWindow.ShowCharacterSelectWindow(UIManager.GetFocusWindow());
        item?.StartChat();
    }
}
