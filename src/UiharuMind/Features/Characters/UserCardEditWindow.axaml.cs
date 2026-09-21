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
    private CharacterDraft? _draft;

    public UserCardEditWindow()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ChatInfoModel>();
    }

    /// <summary>
    /// 交代要编辑的那份草稿。与角色编辑窗一样走草稿-提交：取消就是把它丢掉
    /// </summary>
    /// <param name="draft">用户卡草稿</param>
    public void SetCharacterInfo(CharacterDraft draft)
    {
        _draft = draft;
        DataContext = draft;
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_draft?.TryCommit() == false) return;
        Close();
    }
}