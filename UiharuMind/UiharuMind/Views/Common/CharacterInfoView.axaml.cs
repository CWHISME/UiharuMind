using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using UiharuMind.ViewModels.ViewData;

namespace UiharuMind.Views.Common;

public partial class CharacterInfoView : UserControl
{
    public static readonly StyledProperty<bool> DisplayButtonProperty =
        AvaloniaProperty.Register<CharacterInfoView, bool>(nameof(IsDisplayFuncButton));

    // public static readonly StyledProperty<CharacterInfoViewData> CharacterInfoProperty =
    //     AvaloniaProperty.Register<CharacterInfoView, CharacterInfoViewData>(nameof(CharacterInfo));

    // public CharacterInfoViewData CharacterInfo
    // {
    //     get => GetValue(CharacterInfoProperty);
    //     set => SetValue(CharacterInfoProperty, value);
    // }

    /// <summary>
    /// 是否显示功能按钮
    /// </summary>
    public bool IsDisplayFuncButton
    {
        get => GetValue(DisplayButtonProperty);
        set => SetValue(DisplayButtonProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DisplayButtonProperty)
        {
            var newValue = change.GetNewValue<bool>();
            if (IsInitialized) OperationPanel.IsVisible = newValue;
        }
        // else if (change.Property == CharacterInfoProperty)
        // {
        //     var newValue = change.GetNewValue<CharacterInfoViewData>();
        //     DataContext = newValue;
        // }
    }

    protected override void OnInitialized()
    {
        base.OnInitialized();
        // DataContext = CharacterInfo;
        OperationPanel.IsVisible = IsDisplayFuncButton;
    }

    public CharacterInfoView()
    {
        IsDisplayFuncButton = true;
        InitializeComponent();
    }

    // private void OnSelectedCharacterChanged(CharacterInfoViewData obj)
    // {
    //     DataContext = obj;
    // }

    // protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     base.OnAttachedToVisualTree(e);
    //     var vm = App.ViewModel.GetViewModel<CharacterListViewModel>();
    //     DataContext = vm.SelectedCharacter;
    //     vm.EventOnSelectedCharacterChanged += OnSelectedCharacterChanged;
    // }
    //
    // protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     base.OnDetachedFromVisualTree(e);
    //     var vm = App.ViewModel.GetViewModel<CharacterListViewModel>();
    //     vm.EventOnSelectedCharacterChanged -= OnSelectedCharacterChanged;
    //     DataContext = null;
    // }
}