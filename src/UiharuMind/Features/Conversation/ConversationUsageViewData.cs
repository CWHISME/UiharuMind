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
/// 会话的 token 用量：输入框工具行那个环形进度、点开的面板，以及两种刷新节流。
///
/// 账本由运行侧逐块记准（<see cref="TurnDriver"/> 写的就是 <see cref="Ledger"/>），
/// 这里只管「什么时候、按哪个上限」把它刷到界面上。
///
/// <b>不再有常驻的文本读数</b>：那行数字换成了环（占比一眼看得出，绝对值在面板里）。
/// 输入框草稿的估算也从「每次停顿都数一遍」改成「打开面板时数一次」——前者是整段全文重算，
/// 只为喂一个一直挂在那儿的数字，打字跟不跟手就差在这里
/// </summary>
public sealed partial class ConversationUsageViewData : ObservableObject, IDisposable
{
    private static readonly TimeSpan StreamingDebounce = TimeSpan.FromMilliseconds(250);

    private readonly Func<int> _contextLength; //上限每次现读：顶栏换模型不重建 agent，缓存就是过期的分母
    private readonly Func<string> _modelLabel;
    private readonly Func<int> _generatedImages;
    private readonly Func<string, int> _countTokens;
    private int _draftVersion; //后台计数只采纳最新一次
    private CancellationTokenSource? _streamingDebounce;

    /// <summary>token 账本</summary>
    public TurnUsageLedger Ledger { get; } = new();

    /// <summary>上下文占用的面板数据（环形进度、压缩水位刻度与配色）</summary>
    public ContextUsageViewData Context { get; } = new();

    /// <summary>
    /// 构造
    /// </summary>
    /// <param name="contextLength">取当前有效模型的上下文上限</param>
    /// <param name="modelLabel">取面板上显示的模型名</param>
    /// <param name="generatedImages">取会话累计生图张数（按张计，不在账本里）</param>
    /// <param name="countTokens">输入估算用的计数；缺省用 <see cref="LlmTokenizer.CountTokens"/></param>
    public ConversationUsageViewData(Func<int> contextLength, Func<string> modelLabel, Func<int> generatedImages,
        Func<string, int>? countTokens = null)
    {
        _contextLength = contextLength;
        _modelLabel = modelLabel;
        _generatedImages = generatedImages;
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
        Context.Refresh(Ledger, _modelLabel(), _generatedImages());
    }

    /// <summary>
    /// 合并刷新。流式期间 provider 每个 chunk 都带用量，逐块刷会让环与面板跟着重排而闪烁，
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

        _streamingDebounce = RunAfter(StreamingDebounce, Refresh);
    }

    /// <summary>
    /// 数一次输入框草稿有多大。<b>只在面板打开时调</b>：计数是整段全文重算（首次还要加载词表），
    /// 跟着每个字跑一遍是白烧 CPU
    /// </summary>
    /// <param name="text">输入框全文</param>
    public void CountDraft(string text)
    {
        int version = ++_draftVersion;
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
                // 面板开着的这几百毫秒里用户可能又改了字，只采纳最新那次
                if (version != _draftVersion) return;
                Ledger.InputEstimate = count;
                Refresh();
            });
        });
    }

    /// <summary>
    /// 输入框被清空时把草稿估算归零。不走分词——空文本不必数，而这里每敲一下都会来一次
    /// </summary>
    public void ClearDraft()
    {
        ++_draftVersion;
        if (Ledger.InputEstimate == 0) return;
        Ledger.InputEstimate = 0;
        Refresh();
    }

    /// <summary>从会话本体恢复用量（响应用量不随消息持久化，累计值与最近一次的数记在本体上）</summary>
    /// <param name="session">会话</param>
    public void RestoreFrom(ChatSession session) => Ledger.RestoreFrom(session);

    /// <summary>停掉还没到点的刷新，作废还在后台的计数</summary>
    public void Dispose()
    {
        ++_draftVersion;
        _streamingDebounce?.Cancel();
    }

    private static CancellationTokenSource RunAfter(TimeSpan delay, Action action)
    {
        CancellationTokenSource debounce = new();
        CancellationToken token = debounce.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (token.IsCancellationRequested) return;
            action();
        }, delay);
        return debounce;
    }
}
