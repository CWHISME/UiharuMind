using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core.AI;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Utils;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickChatResultWindow : QuickWindowBase
{
    public QuickChatResultWindow()
    {
        InitializeComponent();
        // SizeToContent = SizeToContent.WidthAndHeight;

        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ScrollViewer);
        _uiUpdater = new UiUpdater<string>(SetContent);
    }

    private ScrollViewerAutoScrollHolder _autoScrollHolder;
    private CancellationTokenSource? _cts;
    private readonly UiUpdater<string> _uiUpdater;

    public bool IsFinished
    {
        get => !InAnswerPanel.IsVisible;
        private set => InAnswerPanel.IsVisible = !value;
    }

    public async void SetRequestInfo(string info)
    {
        TitleTextBlock.Text = "解释";
        if (LlmManager.Instance.CurrentRunningModel == null)
        {
            SetContent("current model is not loaded");
            IsFinished = true;
            return;
        }

        IsFinished = false;
        SetContent("");
        ChatHistory history = new ChatHistory();
        history.AddSystemMessage($"请使用中文详细解释：{info}。");
        _cts = new CancellationTokenSource();

        await foreach (string result in LlmManager.Instance.CurrentRunningModel.SendMessageAsync(history, _cts.Token))
        {
            await _uiUpdater.UpdateValue(result);
        }

        IsFinished = true;
    }

    protected override void OnPreShow()
    {
        this.SetWindowToMousePosition(HorizontalAlignment.Center);
    }

    protected override void OnPreClose()
    {
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
    }

    private void SetContent(string str)
    {
        ResultTextBlock.Text = str;
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