using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common.ChatPlugins;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.Views.Chat.ChatPlugins;

public partial class ChatPlugin_ChatCharacterInfo : UserControl
{
    public ChatPlugin_ChatCharacterInfo()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_ChatCharacterInfoData : ChatPluginDataBase<ChatPlugin_ChatCharacterInfo>
{
    [ObservableProperty] private string _characterName;
    [ObservableProperty] private string _characterDescription;
    // [ObservableProperty] private string _characterTemplete;

    protected override void OnChatSessionChanged(ChatSessionViewData chatSessionViewData)
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
        CharacterEditWindow.Show(new CharacterInfoViewData(ChatSessionCurrentViewData.ChatSession.CharacterData),
            x => x.SaveCharacter());
    }
}