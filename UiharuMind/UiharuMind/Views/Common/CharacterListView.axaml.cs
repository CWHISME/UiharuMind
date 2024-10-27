using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class CharacterListView : UserControl
{
    public CharacterListView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<CharacterListViewModel>();
    }
}