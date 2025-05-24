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
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using UiharuMind.Utils;
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
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        HistoryView.HistoryListBox.ScrollIntoView(0);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        HistoryView.HistoryListBox.ScrollIntoView(0);
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