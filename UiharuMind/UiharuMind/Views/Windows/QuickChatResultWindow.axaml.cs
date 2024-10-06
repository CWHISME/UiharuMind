using System;
using System.Text;
using System.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils.Tools;
using UiharuMind.Utils;
using UiharuMind.Utils.Tools;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickChatResultWindow : QuickWindowBase
{
    public QuickChatResultWindow()
    {
        InitializeComponent();
        // SizeToContent = SizeToContent.WidthAndHeight;

        // _autoScrollHolder = new ScrollViewerAutoScrollHolder(ResultTextBlock.);
        // _uiUpdater = new ValueUiDelayUpdater<string>(SetContent);
    }

    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;
    // private readonly ValueUiDelayUpdater<string> _uiUpdater;

    private CancellationTokenSource? _cts;

    public bool IsFinished
    {
        get => !InAnswerPanel.IsVisible;
        private set
        {
            InAnswerPanel.IsVisible = !value;
            LoadingEffect.IsLoading = !value;
            ResultTextBlock.IsPlaintext = !value || UiharuCoreManager.Instance.Setting.IsChatPlainText;
        }
    }

    public async void SetRequestInfo(string info)
    {
        TitleTextBlock.Text = "解释";
        if (LlmManager.Instance.CurrentRunningModel == null)
        {
            SetContent(new ChatStreamingMessageInfo("error: current model is not loaded"));
            IsFinished = true;
            return;
        }

        IsFinished = false;
        SetContent(new ChatStreamingMessageInfo());
        ChatHistory history = new ChatHistory();
        history.AddSystemMessage($"你是一位友善、机智、善解人意的机器人，会尽力帮助用户解决问题。请使用中文总结或解释以下文字");
        history.AddUserMessage(info);
        _cts = new CancellationTokenSource();

        LlmManager.Instance.CurrentRunningModel.SendMessageStreaming(history, null,
            SetContent,
            (message) =>
            {
                SetContent(message);
                IsFinished = true;
            }, _cts.Token);
        // await foreach (string result in LlmManager.Instance.CurrentRunningModel.SendMessageAsync(history, _cts.Token))
        // {
        //     // await _uiUpdater.UpdateValue(result);
        //     // Dispatcher.UIThread.InvokeAsync(() => SetContent(result));
        //     SetContent(result);
        // }

        // IsFinished = true;
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetWindowToMousePosition(HorizontalAlignment.Center);
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
    }

    private void SetContent(ChatStreamingMessageInfo info)
    {
        ResultTextBlock.MarkdownText = info.Message;
        // TokenTextBlock.Text = $"(Tokens: {info.TokenCount})";
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        SafeClose();
    }

    private void OnStopButtonClick(object? sender, RoutedEventArgs e)
    {
        _cts.SafeStop();
        IsFinished = true;
    }
}