using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common.ChatPlugins;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.Views.Chat.ChatPlugins;

/// <summary>
/// 编辑角色按钮啥的
/// </summary>
public partial class ChatPlugin_CharacterFuncBtn : UserControl
{
    public ChatPlugin_CharacterFuncBtn()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_CharacterFuncBtnData : ChatPluginDataBase<ChatPlugin_CharacterFuncBtn>
{
    [RelayCommand]
    public void EditCharacter()
    {
        UIManager.ShowEditCharacterWindow(
            new CharacterInfoViewData(ChatSessionCurrentViewData.ChatSession.CharacterData),
            x => x.SaveCharacter());
    }
}