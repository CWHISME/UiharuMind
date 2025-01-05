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
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

/// <summary>
/// 通过快捷键快速打开的一个准备聊天的窗口，含有一个输入框和一个确认按钮
/// </summary>
public partial class QuickStartChatWindow : QuickWindowBase
{
    public static void Show(string? quoteStr = null)
    {
        UIManager.ShowWindow<QuickStartChatWindow>(x =>
        {
            x._quoteStr = quoteStr;
            x._quoteImage = null;
            x.QuoteTextBlock.Text = quoteStr;
            x.QuoteTextBlock.IsVisible = true;
            x.QuoteImage.IsVisible = false;
            x.QuatePanel.IsVisible = !string.IsNullOrEmpty(quoteStr);
        });
    }

    public static void Show(Bitmap? quoteImage)
    {
        UIManager.ShowWindow<QuickStartChatWindow>(x =>
        {
            x._quoteStr = null;
            x._quoteImage = quoteImage;
            x.QuoteTextBlock.IsVisible = false;
            x.QuoteImage.Source = quoteImage;
            x.QuoteImage.IsVisible = true;
            x.QuatePanel.IsVisible = true;
        });
    }

    public QuickStartChatWindow()
    {
        InitializeComponent();

        SendMessageCommand = new RelayCommand(SendInputMessage);

        DataContext = this;

        // this.LostFocus += OnLostFocus;
        // this.Activated += OnOpened;
        // this.Deactivated += OnLostFocus;
    }

    //引用
    private string? _quoteStr;
    private Bitmap? _quoteImage;

    public ICommand SendMessageCommand { get; set; }

    // public override void Awake()
    // {
    //     this.SetSimpledecorationPureWindow();
    //     // base.Awake();
    //     // this.WindowStartupLocation = WindowStartupLocation.Manual;
    //     // this.SizeToContent = SizeToContent.Height;
    //     // this.Opacity = 0;
    // }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        BindMouseClickCloseEvent();
        InputBox.Text = "";
        InitPosition();
    }

    // protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     base.OnAttachedToVisualTree(e);
    //     InitPosition();
    // }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        InitPosition();
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
            ShowMessage("请输入内容！");
            return;
        }

        if (_quoteImage != null)
        {
            ImageVisionSkill skill = new ImageVisionSkill(_quoteImage.BitmapToBytes());
            QuickChatResultWindow.Show("Vision (AI)", inputText, skill);
            CloseByAnimation();
            return;
        }

        // Log.Warning($"Quick chat: {inputText}");
        // UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(inputText));
        // QuickChatResultWindow.Show("询问", $"请根据内容 {_quoteStr} 进行回答：\n{inputText}");
        AgentSkillBase askAgentSkill = string.IsNullOrEmpty(_quoteStr)
            ? new AssistantExpertAgentSkill()
            : new AssistantExpertQuoteAgentSkill(_quoteStr);
        QuickChatResultWindow.Show("Answer", inputText, askAgentSkill);
        CloseByAnimation();
    }

    public void InitPosition()
    {
        // 获取当前激活的屏幕
        var screen = App.ScreensService.MouseScreen;
        if (screen != null)
        {
            // 计算窗口在屏幕中心的坐标
            var x = screen.WorkingArea.Right - (screen.WorkingArea.Width + Width) / 2;
            var y = screen.WorkingArea.Bottom - (screen.WorkingArea.Height) / 2f - Height;

            // 设置窗口位置
            Position = new PixelPoint((int)x, (int)y);
        }
    }
}