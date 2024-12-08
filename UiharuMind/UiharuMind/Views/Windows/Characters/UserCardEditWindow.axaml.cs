using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.Characters;

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
        _characterInfo?.SaveCharacter();
    }
}