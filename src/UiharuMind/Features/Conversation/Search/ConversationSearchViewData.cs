/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.Search;

/// <summary>
/// 会话内搜索栏：关键词、范围开关、结果列表与上下条导航。
///
/// 搜的是历史快照（见 <see cref="SessionSearch"/>），打字停手后在后台跑，旧的一次作废。
/// 只有用户<b>明确要跳</b>（回车、上下条、点结果）才发 <see cref="JumpRequested"/>，选中变化本身不跳——
/// 边打字边跳就是每敲一个字重画一次；同一条再按一次回车也得能再跳（滚走之后想回来）。
/// 结果按新到旧排：会话里最近的那次提及通常就是要找的
/// </summary>
public sealed partial class ConversationSearchViewData : ObservableObject, IDisposable
{
    private static readonly TimeSpan TypingDebounce = TimeSpan.FromMilliseconds(200);

    /// <summary>搜索栏是否打开</summary>
    [ObservableProperty] private bool _isOpen;

    /// <summary>关键词</summary>
    [ObservableProperty] private string _query = string.Empty;

    /// <summary>连思考一起搜</summary>
    [ObservableProperty] private bool _includeThinking;

    /// <summary>连工具调用与检索片段一起搜</summary>
    [ObservableProperty] private bool _includeTools;

    /// <summary>当前选中的结果下标；-1 表示还没跳过</summary>
    [ObservableProperty] private int _selectedIndex = -1;

    /// <summary>「3/17」「17 条」「无匹配」</summary>
    [ObservableProperty] private string _statusText = string.Empty;

    private readonly Func<IReadOnlyList<ChatMessage>?> _history;
    private int _version; //后台搜索只采纳最新一次
    private CancellationTokenSource? _debounce;
    private CancellationTokenSource? _running;

    /// <summary>构造</summary>
    /// <param name="history">当前会话的历史（现取现用：中途换会话也跟得上）；没有会话为 null</param>
    public ConversationSearchViewData(Func<IReadOnlyList<ChatMessage>?> history)
    {
        _history = history;
    }

    /// <summary>结果，新到旧</summary>
    public ObservableCollection<ConversationSearchHitRow> Hits { get; } = new();

    /// <summary>用户要跳到这条命中</summary>
    public event Action<SessionSearchHit>? JumpRequested;

    /// <summary>打开（或再按一次 Ctrl/⌘+F）时请视图把焦点给输入框</summary>
    public event Action? FocusRequested;

    /// <summary>有结果可列（结果列表的显隐）</summary>
    public bool HasHits => Hits.Count > 0;

    /// <summary>打开搜索栏；已开着就只要焦点</summary>
    [RelayCommand]
    public void Open()
    {
        if (!IsOpen)
        {
            IsOpen = true;
            if (!string.IsNullOrWhiteSpace(Query)) Schedule(TimeSpan.Zero); //关掉时结果清了,词还留着
        }

        FocusRequested?.Invoke();
    }

    /// <summary>关闭并清掉结果，关键词留着下次打开接着用</summary>
    [RelayCommand]
    public void Close()
    {
        IsOpen = false;
        Invalidate();
        SetHits([]);
    }

    /// <summary>跳到更早的一条（回车），到头绕回最新</summary>
    [RelayCommand]
    public void Older() => Move(1);

    /// <summary>跳到更新的一条（Shift+回车），到头绕回最早</summary>
    [RelayCommand]
    public void Newer() => Move(-1);

    /// <summary>切换「连思考一起搜」</summary>
    [RelayCommand]
    public void ToggleThinking() => IncludeThinking = !IncludeThinking;

    /// <summary>切换「连工具调用一起搜」</summary>
    [RelayCommand]
    public void ToggleTools() => IncludeTools = !IncludeTools;

    /// <summary>跳到当前选中的那条（点结果行；已选中的那行再点一次也算）</summary>
    public void JumpToSelected()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Hits.Count) JumpRequested?.Invoke(Hits[SelectedIndex].Hit);
    }

    /// <summary>历史变了（追加、删除、换了会话）。开着就按原词重搜，选中原位保住</summary>
    public void NotifyHistoryChanged()
    {
        if (IsOpen && !string.IsNullOrWhiteSpace(Query)) Schedule(TypingDebounce);
        else if (Hits.Count > 0) SetHits([]);
    }

    /// <summary>
    /// 立即搜一次（跳过防抖）。测试用，也给「打开就有词」那一路
    /// </summary>
    /// <returns>搜完（或被更新的一次取代）</returns>
    internal async Task SearchNowAsync()
    {
        _debounce?.Cancel();
        int version = ++_version;
        _running?.Cancel();
        CancellationTokenSource running = _running = new CancellationTokenSource();

        string query = Query;
        SessionSearchOptions options = new(IncludeThinking, IncludeTools);
        // 快照在 UI 线程上取:会话本体那份历史随时在被追加,冷会话还可能被卸载
        List<ChatMessage> snapshot = _history()?.ToList() ?? [];
        List<SessionSearchHit> hits;
        try
        {
            hits = await Task.Run(() => SessionSearch.Find(snapshot, query, options, running.Token), running.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (version != _version) return;
        hits.Reverse();
        SetHits(hits);
    }

    /// <summary>停掉还没到点的搜索，作废还在后台的那次</summary>
    public void Dispose()
    {
        Invalidate();
    }

    partial void OnQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Invalidate();
            SetHits([]);
            return;
        }

        Schedule(TypingDebounce);
    }

    partial void OnIncludeThinkingChanged(bool value) => Schedule(TimeSpan.Zero);

    partial void OnIncludeToolsChanged(bool value) => Schedule(TimeSpan.Zero);

    partial void OnSelectedIndexChanged(int value) => RefreshStatus();

    private void Move(int step)
    {
        if (Hits.Count == 0) return;
        SelectedIndex = SelectedIndex < 0 ? 0 : ((SelectedIndex + step) % Hits.Count + Hits.Count) % Hits.Count;
        JumpToSelected();
    }

    private void Schedule(TimeSpan delay)
    {
        if (!IsOpen || string.IsNullOrWhiteSpace(Query)) return;

        _debounce?.Cancel();
        CancellationTokenSource debounce = _debounce = new CancellationTokenSource();
        CancellationToken token = debounce.Token;
        DispatcherTimer.RunOnce(() =>
        {
            if (!token.IsCancellationRequested) _ = SearchNowAsync();
        }, delay);
    }

    private void Invalidate()
    {
        ++_version;
        _debounce?.Cancel();
        _running?.Cancel();
    }

    private void SetHits(List<SessionSearchHit> hits)
    {
        // 重搜时原位选回上次那条(按消息实例认),不然每来一条新消息,「3/17」就跳回没选
        ChatMessage? selected = SelectedIndex >= 0 && SelectedIndex < Hits.Count ? Hits[SelectedIndex].Hit.Message : null;
        SelectedIndex = -1;
        Hits.Clear();
        foreach (SessionSearchHit hit in hits) Hits.Add(new ConversationSearchHitRow(hit));
        if (selected != null) SelectedIndex = hits.FindIndex(x => ReferenceEquals(x.Message, selected));

        OnPropertyChanged(nameof(HasHits));
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (string.IsNullOrWhiteSpace(Query) || !IsOpen) StatusText = string.Empty;
        else if (Hits.Count == 0) StatusText = Loc.Text(LangKey.ConversationSearchNoMatch);
        else if (SelectedIndex < 0) StatusText = Loc.Text(LangKey.ConversationSearchCount, Hits.Count);
        else StatusText = $"{SelectedIndex + 1}/{Hits.Count}";
    }
}
