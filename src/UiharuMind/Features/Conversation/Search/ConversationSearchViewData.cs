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
using UiharuMind.Shared.Utils;

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
    private readonly LatestOnlyRunner _runner;
    private ConversationSearchReveal? _revealTarget; //跨会话点进来要跳的那条,由下一次采纳的结果兑现

    /// <summary>构造</summary>
    /// <param name="history">当前会话的历史（现取现用：中途换会话也跟得上）；没有会话为 null</param>
    /// <param name="post">回 UI 线程的方式；默认投到界面调度器</param>
    public ConversationSearchViewData(Func<IReadOnlyList<ChatMessage>?> history, Action<Action>? post = null)
    {
        _history = history;
        _runner = new LatestOnlyRunner(post ?? (action => Dispatcher.UIThread.Post(action)));
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
        _revealTarget = null;
        _runner.Cancel();
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

    /// <summary>
    /// 带着关键词打开并跳到指定那条（从跨会话搜索点进来），焦点给搜索框——接着按回车就是在这个会话里往前翻。
    ///
    /// 下标来自历史文件、可能对不上装载后的历史，所以按关键词在会话内重搜，取下标最近的那条；
    /// 会话里已经搜不到就只打开不跳。目标记下、由<b>下一次采纳的结果</b>兑现，而不是等这一次搜完就去读 <see cref="Hits"/>：
    /// 这一次可能被追加消息触发的重搜取代，那时列表里还是上一个词的结果
    /// </summary>
    /// <param name="reveal">要跳的那条</param>
    /// <returns>这一次搜完（或被取代）</returns>
    public Task OpenAtAsync(ConversationSearchReveal reveal)
    {
        IsOpen = true;
        Query = reveal.Query;
        _revealTarget = reveal; //排在改词之后:改词会清掉目标(用户自己改了词就不该再跳)
        FocusRequested?.Invoke();
        return SearchNowAsync();
    }

    /// <summary>历史变了（追加、删除、换了会话）。开着就按原词重搜，选中原位保住</summary>
    public void NotifyHistoryChanged()
    {
        if (IsOpen && !string.IsNullOrWhiteSpace(Query)) Schedule(TypingDebounce);
        else if (Hits.Count > 0) SetHits([]);
    }

    /// <summary>
    /// 立即搜一次（跳过防抖）。测试用，也给「打开就跳」那一路
    /// </summary>
    /// <returns>搜完（或被更新的一次取代）</returns>
    internal Task SearchNowAsync() => _runner.RunNow(SearchAsync);

    /// <summary>最近一次搜索（跑完、被取代都算完成）。测试用</summary>
    internal Task Pending => _runner.Pending;

    /// <summary>停掉还没到点的搜索，作废还在后台的那次</summary>
    public void Dispose() => _runner.Dispose();

    partial void OnQueryChanged(string value)
    {
        _revealTarget = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            _runner.Cancel();
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
        _runner.Schedule(delay, SearchAsync);
    }

    private async Task SearchAsync(CancellationToken token)
    {
        string query = Query;
        SessionSearchOptions options = new(IncludeThinking, IncludeTools);
        // 快照在 UI 线程上取:会话本体那份历史随时在被追加,冷会话还可能被卸载
        List<ChatMessage> snapshot = _history()?.ToList() ?? [];
        List<SessionSearchHit> hits = await Task.Run(() => SessionSearch.Find(snapshot, query, options, token), token);
        if (token.IsCancellationRequested) return;

        hits.Reverse();
        SetHits(hits);
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
        if (_revealTarget is { } target && target.Query == Query) Reveal(target);
    }

    private void Reveal(ConversationSearchReveal target)
    {
        _revealTarget = null;
        if (Hits.Count == 0) return;

        SelectedIndex = Enumerable.Range(0, Hits.Count)
            .MinBy(i => Math.Abs(Hits[i].Hit.MessageIndex - target.MessageIndex)); //一样近取先列出的(新的)那条
        JumpToSelected();
    }

    private void RefreshStatus()
    {
        if (string.IsNullOrWhiteSpace(Query) || !IsOpen) StatusText = string.Empty;
        else if (Hits.Count == 0) StatusText = Loc.Text(LangKey.ConversationSearchNoMatch);
        else if (SelectedIndex < 0) StatusText = Loc.Text(LangKey.ConversationSearchCount, Hits.Count);
        else StatusText = $"{SelectedIndex + 1}/{Hits.Count}";
    }
}
