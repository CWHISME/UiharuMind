using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class CharacterInfoView : UserControl
{
    public CharacterInfoView()
    {
        InitializeComponent();
    }

    private void OnSelectedCharacterChanged(CharacterInfoViewData obj)
    {
        DataContext = obj;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        var vm = App.ViewModel.GetViewModel<CharacterListViewModel>();
        DataContext = vm.SelectedCharacter;
        vm.EventOnSelectedCharacterChanged += OnSelectedCharacterChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        var vm = App.ViewModel.GetViewModel<CharacterListViewModel>();
        vm.EventOnSelectedCharacterChanged -= OnSelectedCharacterChanged;
        DataContext = null;
    }
}