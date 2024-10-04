using System;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 通过快捷键快速打开的一个准备聊天的窗口，含有一个输入框和一个确认按钮
/// </summary>
public partial class QuickStartChatWindow : QuickWindowBase
{
    public QuickStartChatWindow()
    {
        InitializeComponent();

        SendMessageCommand = new RelayCommand(SendInputMessage);

        DataContext = this;

        // this.LostFocus += OnLostFocus;
        // this.Activated += OnOpened;
        // this.Deactivated += OnLostFocus;
    }

    public ICommand SendMessageCommand { get; set; }

    public override void Awake()
    {
        // this.SetSimpledecorationPureWindow();
        base.Awake();
        this.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        this.SizeToContent = SizeToContent.Height;
        // this.Opacity = 0;
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        BindMouseClickCloseEvent();
        InputBox.Text = "";
        // InitPosition();
    }

    // protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     base.OnAttachedToVisualTree(e);
    //     InitPosition();
    // }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InputBox.Focus();
        PlayOpenAnimation();
    }

    private void OnConfirmButtonClick(object sender, RoutedEventArgs e)
    {
        SendInputMessage();
    }

    // private void OnLostFocus(object? sender, EventArgs e)
    // {
    //     // 当窗口失去焦点时自动关闭
    //     // Dispatcher.UIThread.InvokeAsync(Hide);
    //     CloseByAnimation();
    // }

    private void SendInputMessage()
    {
        var inputText = InputBox.Text;
        if (string.IsNullOrEmpty(inputText))
        {
            App.MessageService.ShowMessage("请输入内容！");
            return;
        }

        // Log.Warning($"Quick chat: {inputText}");
        UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(inputText));
        CloseByAnimation();
    }

    // public void InitPosition()
    // {
    //     // 获取当前激活的屏幕
    //     var screen = Screens.ScreenFromVisual(this);
    //     if (screen != null)
    //     {
    //         // 计算窗口在屏幕中心的坐标
    //         var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + Width) / 2;
    //         var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height + Height) / 2;
    //
    //         // 设置窗口位置
    //         Position = new PixelPoint((int)x, (int)y);
    //     }
    // }
}