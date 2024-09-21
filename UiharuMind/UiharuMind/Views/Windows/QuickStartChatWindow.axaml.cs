using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 通过快捷键快速打开的一个准备聊天的窗口，含有一个输入框和一个确认按钮
/// </summary>
public partial class QuickStartChatWindow : Window
{
    public QuickStartChatWindow()
    {
        InitializeComponent();

        this.SetSimpledecorationWindow();

        this.LostFocus += OnLostFocus;
        this.Activated += OnOpened;
        this.Deactivated += OnLostFocus;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        InitPosition();
        InputBox.Text = "";
        InputBox.Focus();
    }

    private void OnConfirmButtonClick(object sender, RoutedEventArgs e)
    {
        var inputText = InputBox.Text;
        Log.Warning($"Quick chat: {inputText}");
        Hide();
    }

    private void OnLostFocus(object? sender, EventArgs e)
    {
        // 当窗口失去焦点时自动关闭
        Dispatcher.UIThread.InvokeAsync(Hide);
    }

    public void InitPosition()
    {
        // 获取当前激活的屏幕
        var screen = Screens.ScreenFromVisual(this);
        if (screen != null)
        {
            // 计算窗口在屏幕中心的坐标
            var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + Width) / 2;
            var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height + Height) / 2;

            // 设置窗口位置
            Position = new PixelPoint((int)x, (int)y);
        }
    }
}