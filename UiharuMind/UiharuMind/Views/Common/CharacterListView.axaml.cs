using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
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
}