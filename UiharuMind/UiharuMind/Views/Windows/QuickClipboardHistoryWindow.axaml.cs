using System;
using Avalonia.Input;
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
        BindMouseClickCloseEvent();
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
}