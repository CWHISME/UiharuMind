using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 当复制操作发生后，显示在复制位置的工具
/// </summary>
public partial class QuickToolWindow : Window
{
    public QuickToolWindow()
    {
        InitializeComponent();

        SizeToContent = SizeToContent.WidthAndHeight;
        this.SetSimpledecorationWindow();

        this.LostFocus += OnLostFocus;
        this.Activated += OnOpened;
        this.Deactivated += OnLostFocus;
    }

    public void SetAnswerString(string text)
    {
        Log.Debug("Set answer string: " + text);
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        this.SetWindowToMousePosition(HorizontalAlignment.Right, offsetX: 5, offsetY: -5);
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.InvokeAsync(Hide);
    }

    private void OnOcrBtnClock(object? sender, RoutedEventArgs e)
    {
        new QuickChatResultWindow().Show();
    }

    private void OnCopyBtnClock(object? sender, RoutedEventArgs e)
    {
        Log.Debug("Copy button clicked");
    }
}