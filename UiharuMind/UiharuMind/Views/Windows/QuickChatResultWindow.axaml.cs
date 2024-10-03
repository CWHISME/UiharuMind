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
        private set => InAnswerPanel.IsVisible = !value;
    }

    public async void SetRequestInfo(string info)
    {
        TitleTextBlock.Text = "解释";
        if (LlmManager.Instance.CurrentRunningModel == null)
        {
            SetContent("error: current model is not loaded");
            IsFinished = true;
            return;
        }

        IsFinished = false;
        SetContent("");
        ChatHistory history = new ChatHistory();
        history.AddSystemMessage($"请使用中文解释：{info} 的意思，不要使用英文");
        _cts = new CancellationTokenSource();

        await foreach (string result in LlmManager.Instance.CurrentRunningModel.SendMessageAsync(history, _cts.Token))
        {
            // await _uiUpdater.UpdateValue(result);
            Dispatcher.UIThread.InvokeAsync(() => SetContent(result));
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
        ResultTextBlock.Markdown = str;
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