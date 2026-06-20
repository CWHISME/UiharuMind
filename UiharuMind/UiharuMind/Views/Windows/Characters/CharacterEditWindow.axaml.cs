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
    public Action<CharacterInfoViewData>? OnSureCallback;
    private Button? _selectedSectionButton;

    public CharacterEditWindow()
    {
        InitializeComponent();
        SetSelectedSectionButton(BasicSectionButton);
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

    private void SectionButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        SetSelectedSectionButton(button);
        EditView.ScrollToSection(button.Tag as string);
    }

    private void SetSelectedSectionButton(Button button)
    {
        _selectedSectionButton?.Classes.Remove("selected");
        _selectedSectionButton = button;
        _selectedSectionButton.Classes.Add("selected");
    }
}
