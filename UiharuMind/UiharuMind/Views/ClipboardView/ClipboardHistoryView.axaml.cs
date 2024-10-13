/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ViewData.ClipboardViewData;
using Ursa.Controls;

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

    private async void MenuItemDeleteAll_Click(object? sender, RoutedEventArgs e)
    {
        var result =
            await App.MessageService.ShowConfirmMessageBox(Lang.DeleteAllClipboardHistoryTips, this.GetParentWindow());
        if (result == MessageBoxResult.Yes)
        {
            App.ViewModel.GetViewModel<ClipboardHistoryViewModel>().DeleteAll();
        }
    }
}