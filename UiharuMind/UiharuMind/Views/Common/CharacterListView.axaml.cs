using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Resources.Lang;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class CharacterListView : UserControl
{
    // public static readonly StyledProperty<CharacterInfoViewData> SelectCharacterProperty =
    //     AvaloniaProperty.Register<CharacterListView, CharacterInfoViewData>(nameof(SelectCharacter));
    //
    // public static readonly StyledProperty<bool> Property =
    //     AvaloniaProperty.Register<CharacterListView, CharacterInfoViewData>(nameof(SelectCharacter));
    //
    // public CharacterInfoViewData SelectCharacter
    // {
    //     get => GetValue(SelectCharacterProperty);
    //     set => SetValue(SelectCharacterProperty, value);
    // }
    //
    // protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    // {
    //     base.OnPropertyChanged(change);
    //     if (change.Property == SelectCharacterProperty)
    //     {
    //         var newInfo = change.GetNewValue<CharacterInfoViewData>();
    //         App.ViewModel.GetViewModel<CharacterListViewData>().SelectedCharacter = newInfo;
    //     }
    // }

    public CharacterListView()
    {
        InitializeComponent();

        // DataContext = App.ViewModel.GetViewModel<CharacterListViewData>();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        var data = DataContext as CharacterListViewData;
        if (data == null) return;
        FilterPanel.Children.Clear();
        for (int i = 0; i < data.FilterTags.Length; i++)
        {
            var tag = data.FilterTags[i];
            var button = new ToggleButton();
            button.Content = tag;
            button.IsChecked = tag == data.FilterTag;
            button.Click += OnFilterClick;
            FilterPanel.Children.Add(button);
        }
    }

    private void OnFilterClick(object? sender, RoutedEventArgs e)
    {
        var content = (sender as ToggleButton)?.Content;
        string? tag = content as string;
        foreach (var child in FilterPanel.Children)
        {
            var button = child as ToggleButton;
            if (button == null) continue;
            button.IsChecked = button.Content == content;
        }

        var data = DataContext as CharacterListViewData;
        if (data == null) return;
        data.FilterTag = tag ?? "";
    }
}