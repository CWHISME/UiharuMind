using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core;
using UiharuMind.Features.Conversation;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.Features.Characters;

public partial class UserCardEditWindow : UiharuWindowBase
{
    private CharacterInfoViewData? _characterInfo;

    public UserCardEditWindow()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ChatInfoModel>();
    }

    public void SetCharacterInfo(CharacterInfoViewData characterInfo)
    {
        _characterInfo = characterInfo;
        DataContext = characterInfo;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_characterInfo?.CheckCharacterNameValid() == true) _characterInfo?.SaveCharacter();
    }
}