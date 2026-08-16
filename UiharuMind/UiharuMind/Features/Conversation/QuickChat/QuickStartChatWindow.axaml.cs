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
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.Conversation.QuickChat;

/// <summary>
/// 通过快捷键快速打开的一个准备聊天的窗口，含有一个输入框和一个确认按钮
/// </summary>
public partial class QuickStartChatWindow : QuickWindowBase
{
    public static void Show(string? quoteStr = null)
    {
        UIManager.ShowWindow<QuickStartChatWindow>(x =>
        {
            x.ResetInfo();
            x._quoteStr = quoteStr;
            x._quoteImage = null;
            x.QuoteTextBlock.Text = quoteStr;
            x.QuoteTextBlock.IsVisible = true;
            x.QuoteImage.IsVisible = false;
            x.QuatePanel.IsVisible = !string.IsNullOrEmpty(quoteStr);
        });
    }

    /// <summary>
    /// 带一张引用图打开快速提问窗。<b>本窗接管这张位图</b>：它是缓存窗，隐藏后仍活着，
    /// 所以引用图要留到下一次 <c>Show</c> 才随 <see cref="ResetInfo"/> 释放。
    /// 调用方手上那张图若另有所有者（如预览窗），请克隆一份再交进来——
    /// 否则预览窗一关就释放，这里发送时读到的是已释放的位图。
    /// </summary>
    /// <param name="quoteImage">引用图，所有权移交本窗</param>
    public static void Show(Bitmap? quoteImage)
    {
        UIManager.ShowWindow<QuickStartChatWindow>(x =>
        {
            x.ResetInfo();
            x._quoteStr = null;
            x._quoteImage = quoteImage;
            x.QuoteTextBlock.IsVisible = false;
            x.QuoteImage.Source = quoteImage;
            x.QuoteImage.IsVisible = true;
            x.QuatePanel.IsVisible = true;
        });
    }

    protected override bool IsAllowFocusOnOpen => true;

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
        this.SetScreenCenterPosition();
    }

    // protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    // {
    //     base.OnAttachedToVisualTree(e);
    //     InitPosition();
    // }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        this.SetScreenCenterPosition();
        InputBox.Focus();
        // PlayOpenAnimation(() => { InputBox.Focus(); });
    }

    private void ResetInfo()
    {
        // 引用图归本窗所有(见 Show(Bitmap?))。缓存窗不会真的关闭,不在这里释放就是一路留到进程结束。
        // 先摘绑定再释放:顺序反了,已释放的位图还挂在 Image.Source 上,下一帧渲染就撞上去
        Bitmap? staleQuoteImage = _quoteImage;
        QuoteImage.Source = null;
        _quoteImage = null;
        staleQuoteImage?.Dispose();

        QuoteTextBlock.Text = "";
        _quoteStr = null;
        InputBox.Text = "";
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
            ImageVisionPromptAction skill = new ImageVisionPromptAction(_quoteImage.BitmapToBytes());
            QuickChatResultWindow.Show("Vision (AI)", inputText, skill);
            CloseByAnimation();
            return;
        }

        // Log.Warning($"Quick chat: {inputText}");
        // UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(inputText));
        // QuickChatResultWindow.Show("询问", $"请根据内容 {_quoteStr} 进行回答：\n{inputText}");
        PromptActionBase askAgentSkill = string.IsNullOrEmpty(_quoteStr)
            ? new AssistantExpertPromptAction()
            : new AssistantExpertQuotePromptAction(_quoteStr);
        QuickChatResultWindow.Show("Answer", inputText, askAgentSkill);
        CloseByAnimation();
    }
}