using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows.Characters;

public partial class CharacterEditWindow : UiharuWindowBase
{
    public static void Show(CharacterInfoViewData? characterInfo, Action<CharacterInfoViewData>? onSureCallback)
    {
        characterInfo ??= new CharacterInfoViewData();
        UIManager.ShowWindow<CharacterEditWindow>(x =>
        {
            x.DataContext = characterInfo;
            x._onSureCallback = onSureCallback;
        });
    }

    private Action<CharacterInfoViewData>? _onSureCallback;

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
            characterInfo.SaveCharacter();
            _onSureCallback?.Invoke(characterInfo);
        }

        Close();
    }
}