using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Features.Characters;

public partial class CharacterEditWindow : UiharuWindowBase
{
    public Action<CharacterInfoViewData>? OnSureCallback;

    public CharacterEditWindow()
    {
        InitializeComponent();
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is CharacterInfoViewData characterInfo)
        {
            if (!characterInfo.CheckCharacterNameValid()) return;
            // if (_onSureCallback == null) characterInfo.SaveCharacter();
            OnSureCallback?.Invoke(characterInfo);
        }

        Close();
    }
}
