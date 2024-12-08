using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Windows.Characters;

namespace UiharuMind.Views.Common.ChatPlugins;

public partial class ChatPlugin_UserCharacterCard : UserControl
{
    public ChatPlugin_UserCharacterCard()
    {
        InitializeComponent();
    }
}

public partial class ChatPlugin_UserCharacterCardData : ChatPluginDataBase<ChatPlugin_UserCharacterCard>
{
    private CharacterInfoViewData _user;
    public CharacterInfoViewData User => _user;

    public string UserTemplateReadonly =>
        string.IsNullOrEmpty(User.Template) ? "无" : User.Template.Replace("{{$char}}", User.Description);

    public ChatPlugin_UserCharacterCardData()
    {
        _user = new CharacterInfoViewData(CharacterManager.Instance.UserCharacterData);
    }

    [RelayCommand]
    public void EditUserCard()
    {
        UIManager.ShowWindow<UserCardEditWindow>(x => x.SetCharacterInfo(User));
    }
}