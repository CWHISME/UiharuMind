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
using System.ClientModel;
using System.Collections.ObjectModel;
using System.Text;
using System.Threading;
using Microsoft.Extensions.AI;
using UiharuMind.Features.Conversation.Items;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Utils.Tools;
using UiharuMind.Shared.UIHolder;
using UiharuMind.Shared.Windows;
using UiharuMind.Shared.Shell;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Configs;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils.Tools;
using UiharuMind.Core;

namespace UiharuMind.Features.Conversation.QuickChat;

public partial class QuickChatResultWindow : QuickWindowBase
{
    // public static void Show(string? title, string? answer, string? prompt = null)
    // {
    //     if (answer == null) return;
    //     UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(title, answer, prompt), null,
    //         ConfigManager.Instance.ChatSetting.IsAllowMultiAnswerWindow);
    // }

    public static void Show(string? title, string? answer, PromptActionBase agentSkill)
    {
        if (answer == null) return;
        UIManager.ShowWindow<QuickChatResultWindow>(x => x.SetRequestInfo(title, answer, agentSkill), null,
            ConfigManager.Instance.ChatSetting.IsAllowMultiAnswerWindow);
    }

    public QuickChatResultWindow()
    {
        InitializeComponent();
        // SizeToContent = SizeToContent.WidthAndHeight;

        ThinkingList.ItemsSource = _thinkingItems;
        _autoScrollHolder = new ScrollViewerAutoScrollHolder(ScrollViewer);
        PlaintextCheckBox.IsChecked = ChatSettingConfig.Current.IsChatPlainText;
        PlaintextCheckBox.IsCheckedChanged += (sender, args) =>
        {
            var isChecked = (sender as CheckBox)?.IsChecked ?? false;
            ResultTextBlock.IsPlaintext = isChecked;
            ChatSettingConfig.Current.IsChatPlainText = isChecked;
            ChatSettingConfig.Current.Save();
        };
        // _uiUpdater = new ValueUiDelayUpdater<string>(SetContent);
    }

    private readonly ScrollViewerAutoScrollHolder _autoScrollHolder;
    // private readonly ValueUiDelayUpdater<string> _uiUpdater;

    /// <summary>
    /// 本次回答里的思考段。与会话流同一套条目与卡片，只是这里没有工具调用，
    /// 分段只由「思考与正文交替」产生
    /// </summary>
    private readonly ObservableCollection<ThinkingItem> _thinkingItems = [];

    private readonly ThinkTagStreamParser _thinkParser = new(); //本地模型把 <think> 混在正文流里
    private ThinkingItem? _streamingThinking;

    private CancellationTokenSource? _cts;

    public bool IsFinished
    {
        get => !InAnswerPanel.IsVisible;
        private set
        {
            InAnswerPanel.IsVisible = !value;
            LoadingEffect.IsLoading = !value;
            RegenerateButton.IsVisible = value;
            ToolPanel.IsVisible = value;
            ResultTextBlock.IsPlaintext =
                ChatSettingConfig.Current
                    .IsChatPlainText; //!value || ConfigManager.Instance.Setting.IsChatPlainText;
        }
    }

    /// <summary>
    /// 是否能转换为临时对话
    /// </summary>
    public bool IsChatConvertable => _agentSkill.IsConvertableToChatSession;

    private string _askContent = string.Empty;
    private PromptActionBase _agentSkill = null!;

    public void SetRequestInfo(string? title, string content, PromptActionBase agentSkill)
    {
        TitleTextBlock.Text = title ?? Lang.DefaultQuickChatTitle;
        SetContent("");
        _askContent = content;
        _agentSkill = agentSkill;

        _cts = new CancellationTokenSource();
        IsFinished = false;

        async void Action()
        {
            try
            {
                //讨论模式
                await foreach (var item in agentSkill.RunContentAsync(content, _cts.Token))
                {
                    ApplyContent(item);
                }
            }
            catch (ClientResultException e)
            {
                Log.Error(e.Message);
            }
            catch (Exception e)
            {
                Log.Warning(e.Message);
            }

            // 取消与出错也要收尾:解析器里可能还压着半段文本,思考段也得停在折叠状态
            _thinkParser.Complete(AppendContent, AppendThinking);
            CloseThinking();
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
        PlaintextCheckBox.IsChecked = ChatSettingConfig.Current.IsChatPlainText;
    }

    protected override void OnPreClose()
    {
        base.OnPreClose();
        if (_cts?.IsCancellationRequested == false) _cts?.Cancel();
    }

    private void SetContent(string info)
    {
        ResultTextBlock.ForceSetText(info);
        _thinkParser.Reset();
        _streamingThinking = null;
        _thinkingItems.Clear();
        // TokenTextBlock.Text = $"(Tokens: {info.TokenCount})";
    }

    /// <summary>
    /// 装配一段内容。与会话流的口径一致：思考既可能是结构化的
    /// <see cref="TextReasoningContent"/>，也可能是本地模型混在正文里的 &lt;think&gt; 段
    /// </summary>
    /// <param name="content">来自技能的一段内容</param>
    private void ApplyContent(AIContent content)
    {
        switch (content)
        {
            case TextReasoningContent { Text.Length: > 0 } reasoning:
                AppendThinking(reasoning.Text);
                break;
            case TextContent { Text.Length: > 0 } text:
                _thinkParser.Feed(text.Text, AppendContent, AppendThinking);
                break;
        }
    }

    private void AppendContent(string info)
    {
        // 正文一出现就说明上一段想完了
        CloseThinking();
        ResultTextBlock.AppendText(info);
    }

    private void AppendThinking(string delta)
    {
        // 流式进行中保持展开,能看到它在想什么;收尾时才按设置折叠
        if (_streamingThinking == null) _thinkingItems.Add(_streamingThinking = new ThinkingItem { IsExpanded = true });
        _streamingThinking.Append(delta);
    }

    private void CloseThinking()
    {
        if (_streamingThinking == null) return;
        _streamingThinking.Flush();
        _streamingThinking.IsExpanded = !ChatSettingConfig.Current.IsChatAutoCollapseThinking;
        _streamingThinking = null;
    }

    private void InputElement_OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        this.BeginMoveDrag(e);
        PointerUpdateKind pointerUpdateKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
        if (pointerUpdateKind == PointerUpdateKind.LeftButtonPressed && e.ClickCount >= 2)
        {
            if (Math.Abs(Height - StartHeight) > 10)
            {
                Width = StartWidth;
                Height = StartHeight;
            }
            else WindowState = WindowState.Maximized;
        }
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

        QuickChatViewWindow.Show(chatSession);
    }
}