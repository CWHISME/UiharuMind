using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Shell;
using UiharuMind.Features.Characters;
using UiharuMind.Features.Conversation;

namespace UiharuMind.Features.Conversation.ChatPlugins;

public partial class ChatPlugin_ChatCharacterInfo : UserControl
{
    public ChatPlugin_ChatCharacterInfo()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_ChatCharacterInfoData : ChatPluginDataBase<ChatPlugin_ChatCharacterInfo>
{
    [ObservableProperty] private string _characterName = string.Empty;
    [ObservableProperty] private string _characterDescription = string.Empty;
    // [ObservableProperty] private string _characterTemplete;

    protected override void OnChatSessionChanged(ChatSessionItemViewData chatSessionViewData)
    {
        base.OnChatSessionChanged(chatSessionViewData);
        CharacterName = chatSessionViewData.ChatSession.CharacterData.CharacterName;
        // CharacterTemplete = ChatSessionCurrentViewData.ChatSession.CharacterData.TryRender(ChatSessionCurrentViewData
        //     .ChatSession
        //     .CharacterData.Template);
        CharacterDescription = chatSessionViewData.ChatSession.CharacterData.Description;
    }

    [RelayCommand]
    public void EditCharacter()
    {
        UIManager.ShowEditCharacterWindow(
            new CharacterInfoViewData(ChatSessionCurrentViewData.ChatSession.CharacterData),
            x => x.SaveCharacter());
    }
}
