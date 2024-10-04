using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.ViewModels.ViewData.ClipboardViewData;

namespace UiharuMind.Views.ClipboardView;

public partial class ClipboardHistoryView : UserControl
{
    public ClipboardHistoryView()
    {
        InitializeComponent();

        DataContext = App.ViewModel.GetViewModel<ClipboardHistoryViewModel>();
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
            .Copy((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    }

    private void MenuItemDelete_Click(object? sender, RoutedEventArgs e)
    {
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>()
            .Delete((ClipboardItem)((Control)(e.Source!))!.DataContext!);
    }
}