/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Features.Conversation.SidePanels;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话的 token 用量显示：工具行那一行文本、悬停面板，以及两种刷新节流。
///
/// 账本由运行侧逐块记准（<see cref="TurnDriver"/> 写的就是 <see cref="Ledger"/>），
/// 这里只管「什么时候、按哪个上限」把它刷到界面上。
/// </summary>
public sealed partial class ConversationUsageViewData : ObservableObject, IDisposable
{
    private static readonly TimeSpan TypingDebounce = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan StreamingDebounce = TimeSpan.FromMilliseconds(250);

    /// <summary>工具行的用量文本（占用/上限、输入估算）</summary>
    [ObservableProperty] private string _text = string.Empty;

    private readonly Func<int> _contextLength; //上限每次现读：顶栏换模型不重建 agent，缓存就是过期的分母
    private readonly Func<string> _modelLabel;
    private readonly Func<string, int> _countTokens;
    private int _inputEstimateVersion; //后台计数只采纳最新一次
    private CancellationTokenSource? _typingDebounce;
    private CancellationTokenSource? _streamingDebounce;

    /// <summary>token 账本</summary>
    public TurnUsageLedger Ledger { get; } = new();

    /// <summary>上下文占用的悬停面板（进度条、压缩水位刻度与配色）</summary>
    public ContextUsageViewData Context { get; } = new();

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="contextLength">取当前有效模型的上下文上限</param>
    /// <param name="modelLabel">取悬停面板上显示的模型名</param>
    /// <param name="countTokens">输入估算用的计数；缺省用 <see cref="LlmTokenizer.CountTokens"/></param>
    public ConversationUsageViewData(Func<int> contextLength, Func<string> modelLabel,
        Func<string, int>? countTokens = null)
    {
        _contextLength = contextLength;
        _modelLabel = modelLabel;
        _countTokens = countTokens ?? LlmTokenizer.CountTokens;
    }

    /// <summary>立即刷新。可从任意线程调用：模型就绪的通知来自后台线程的异步续体</summary>
    public void Refresh()
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Refresh);
            return;
        }

        Ledger.ContextLength = _contextLength();
        Text = Ledger.Text;
        Context.Refresh(Ledger, _modelLabel());
    }

    /// <summary>
    /// 合并刷新。流式期间 provider 每个 chunk 都带用量，逐块刷会让状态栏与悬停面板跟着重排而闪烁，
    /// 这里收进一个窗口只刷一次
    /// </summary>
    /// <param name="force">跳过合并立即刷（流结束时补最终值，不能再拖一个窗口）</param>
    public void RefreshCoalesced(bool force = false)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => RefreshCoalesced(force));
            return;
        }

        _streamingDebounce?.Cancel();
        if (force)
        {
            Refresh();
            return;
        }

        _streamingDebounce = RefreshAfter(StreamingDebounce);
    }

    /// <summary>
    /// 按输入框文本重估输入 token。计数在后台跑（首次会加载词表），停手后才刷新，
    /// 免得悬停面板跟着每个字符重排
    /// </summary>
    /// <param name="text">输入框全文</param>
    public void EstimateInput(string text)
    {
        int version = ++_inputEstimateVersion;
        if (string.IsNullOrEmpty(text))
        {
            Ledger.InputEstimate = 0;
            Refresh();
            return;
        }

        _ = Task.Run(() =>
        {
            int count = _countTokens(text);
            Dispatcher.UIThread.Post(() =>
            {
                if (version != _inputEstimateVersion) return;
                Ledger.InputEstimate = count;
                _typingDebounce?.Cancel();
                _typingDebounce = RefreshAfter(TypingDebounce);
            });
        });
    }

    /// <summary>从会话本体恢复累计用量（响应用量不随消息持久化，累计值记在本体上）</summary>
    /// <param name="session">会话</param>
    public void RestoreFrom(ChatSession session)
    {
        Ledger.RestoreSession(session.TotalInputTokens, session.TotalOutputTokens, session.LastInputTokens,
            session.TotalReasoningTokens);
    }

    /// <summary>停掉还没到点的刷新</summary>
    public void Dispose()
    {
        _typingDebounce?.Cancel();
        _streamingDebounce?.Cancel();
    }

    private CancellationTokenSource RefreshAfter(TimeSpan delay)
    {
        CancellationTokenSource debounce = new();
        CancellationToken token = debounce.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (token.IsCancellationRequested) return;
            Refresh();
        }, delay);
        return debounce;
    }
}
