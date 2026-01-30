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

using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ViewData.ClipboardViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickClipboardHistoryWindow : QuickWindowBase
{
    public QuickClipboardHistoryWindow()
    {
        InitializeComponent();
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetWindowToMousePosition(HorizontalAlignment.Right, VerticalAlignment.Center);
        // BindMouseClickCloseEvent();
        App.ViewModel.GetViewModel<ClipboardHistoryViewModel>().SyncData();
        HistoryView.HistoryListBox.ScrollIntoView(0);
    }

    protected override void IsVisibleChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.IsVisibleChanged(e);
        if (e.GetNewValue<bool>())
        {
            // 使用Dispatcher延迟处理，确保布局已完成
            Dispatcher.UIThread.InvokeAsync(() => { HistoryView.HistoryListBox.ScrollIntoView(0); },
                DispatcherPriority.ApplicationIdle);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}