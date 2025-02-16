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
using System.Text;
using System.Threading;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using UiharuMind.Core;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character.Skills;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils.Tools;
using UiharuMind.Resources.Lang;
using UiharuMind.Utils;
using UiharuMind.Utils.Tools;
using UiharuMind.ViewModels.UIHolder;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;

namespace UiharuMind.Views.Windows;

public partial class QuickChatResultWindow : QuickWindowBase
{
    // public static void Show(string? title, string? answer, string? prompt = null)
    // {
    //     if (answer == null) return;
    //     UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(title, answer, prompt), null,
    //         ConfigManager.Instance.ChatSetting.IsAllowMultiAnswerWindow);
    // }

    public static void Show(string? title, string? answer, AgentSkillBase agentSkill)
    {
        if (answer == null) return;
        UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(title, answer, agentSkill), null,
            ConfigManager.Instance.ChatSetting.IsAllowMultiAnswerWindow);
    }

    public QuickChatResultWindow()
    {
        InitializeComponent();
        // SizeToContent = SizeToContent.WidthAndHeight;

        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ScrollViewer);
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
            RegenerateButton.IsVisible = value;
            FuncBtn.IsVisible = value;
            ResultTextBlock.IsPlaintext =
                ConfigManager.Instance.Setting
                    .IsChatPlainText; //!value || ConfigManager.Instance.Setting.IsChatPlainText;
        }
    }

    /// <summary>
    /// 是否能转换为临时对话
    /// </summary>
    public bool IsChatConvertable
    {
        get { return _agentSkill.IsConvertableToChatSession; }
    }

    private string _askContent;
    private AgentSkillBase _agentSkill;

    // public void SetRequestInfo(string? title, string content, string? prompt = null)
    // {
    //     TitleTextBlock.Text = title ?? Lang.DefaultQuickChatTitle;
    //
    //     _cts = new CancellationTokenSource();
    //     IsFinished = false;
    //
    //     async void Action()
    //     {
    //         try
    //         {
    //             //简单模式
    //             await foreach (var message in LlmManager.Instance.CurrentRunningModel!
    //                                .InvokeQuickToolPromptStreamingAsync(
    //                                    content, prompt, Lang.Culture, _cts.Token))
    //             {
    //                 AppendContent(message);
    //             }
    //         }
    //         catch (Exception e)
    //         {
    //             Log.Warning(e.Message);
    //         }
    //
    //         IsFinished = true;
    //     }
    //
    //     Dispatcher.UIThread.Post(Action, DispatcherPriority.ApplicationIdle);
    // }

    public void SetRequestInfo(string? title, string content, AgentSkillBase agentSkill)
    {
        TitleTextBlock.Text = title ?? Lang.DefaultQuickChatTitle;
        _askContent = content;
        _agentSkill = agentSkill;

        _cts = new CancellationTokenSource();
        IsFinished = false;

        async void Action()
        {
            try
            {
                //讨论模式
                await foreach (var message in agentSkill.DoSkill(LlmManager.Instance.CurrentRunningModel!, content,
                                   _cts.Token))
                {
                    AppendContent(message);
                }
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
            }

            IsFinished = true;
        }

        Dispatcher.UIThread.Post(Action, DispatcherPriority.ApplicationIdle);
    }

    public override void Awake()
    {
        base.Awake();
        CanResize = true;
    }

    protected override void OnPreShow()
    {
        base.OnPreShow();
        this.SetWindowToMousePosition(HorizontalAlignment.Center);
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        SetContent("");
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
    }

    private void SetContent(string info)
    {
        ResultTextBlock.SimpleSetMarkdownText = info;
        // TokenTextBlock.Text = $"(Tokens: {info.TokenCount})";
    }

    private void AppendContent(string info)
    {
        ResultTextBlock.SimpleSetMarkdownText = (info);
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

    private void OnRegenerateButtonClick(object? sender, RoutedEventArgs e)
    {
        SetRequestInfo(TitleTextBlock.Text, _askContent, _agentSkill);
    }

    private void OnConvertToTempChatButtonClick(object? sender, RoutedEventArgs e)
    {
        if (!IsChatConvertable) return;
        var chatSession = _agentSkill.TryConvertToChatSession();
        if (chatSession == null)
        {
            Log.Error("Failed to convert to chat session.");
            return;
        }

        QuickChatViewWindow.Show(new ChatViewModel()
            { ChatSession = new ChatSessionViewData(chatSession) });
    }
}