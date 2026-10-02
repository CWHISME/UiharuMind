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
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Chat.Search;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表搜索框的「消息里提到的」那一段：同一个搜索词，除了按标题滤列表，再在后台扫一遍这一侧全部会话的消息（见 ADR 0057）。
///
/// 要用户<b>点一下才扫</b>（<see cref="Start"/>，或在搜索框里回车）：多数时候打字只是想按标题找会话，
/// 每敲一个字都扫几百个文件是白花的。开扫之后跟着改词重扫，清空搜索框就收起、下次再要点。
/// 点一条摘要发 <see cref="OpenRequested"/>，由页面切过去并在会话内跳到那条
/// </summary>
public sealed partial class SessionContentSearchViewData : ObservableObject, IDisposable
{
    private const int SnippetsPerSession = 3; //侧栏窄,每个会话只列最近几次提及,其余进去按回车翻
    private static readonly TimeSpan TypingDebounce = TimeSpan.FromMilliseconds(300); //比会话内长:一次要扫几百个文件

    /// <summary>后台正在扫</summary>
    [ObservableProperty] private bool _isSearching;

    /// <summary>这一段的标题行：「消息里提到的（3 个会话）」「正在搜索消息…」「消息里也没有」</summary>
    [ObservableProperty] private string _headerText = string.Empty;

    private readonly Func<IReadOnlyList<SessionListItem>> _sessions;
    private readonly Func<string, IEnumerable<string>> _readHistoryLines;
    private readonly LatestOnlyRunner _runner;
    private string _query = string.Empty;
    private bool _isStarted; //用户点过「在消息里搜索」,清空搜索框才复位

    /// <summary>构造</summary>
    /// <param name="sessions">要扫的会话（这一侧的全部，不只是标题匹配的），按列表顺序</param>
    /// <param name="readHistoryLines">读一个会话的历史行（生产 <see cref="SessionManager.ReadHistoryLines"/>）</param>
    /// <param name="post">回 UI 线程的方式</param>
    public SessionContentSearchViewData(Func<IReadOnlyList<SessionListItem>> sessions,
        Func<string, IEnumerable<string>> readHistoryLines, Action<Action> post)
    {
        _sessions = sessions;
        _readHistoryLines = readHistoryLines;
        _runner = new LatestOnlyRunner(post);
    }

    /// <summary>有命中的会话，按列表顺序</summary>
    public ObservableCollection<SessionContentResultRow> Results { get; } = new();

    /// <summary>这一段要不要显示：开扫了且有搜索词（没搜到也要告诉用户消息里也没有）</summary>
    public bool IsActive => _isStarted && _query.Length > 0;

    /// <summary>「在消息里搜索」那一行要不要显示：有搜索词、还没开扫</summary>
    public bool CanStart => !_isStarted && _query.Length > 0;

    /// <summary>「在消息里搜索「词」」</summary>
    public string StartText => Loc.Text(LangKey.SessionContentSearchStart, _query);

    /// <summary>最近一次扫描（跑完、被取代都算完成）。测试等它，不必真的睡过防抖</summary>
    internal Task Pending => _runner.Pending;

    /// <summary>用户点了一条摘要：切到它所在的会话并跳到那条</summary>
    public event Action<SessionContentHitRow>? OpenRequested;

    /// <summary>搜索词变了：清空时立即收起并复位，开扫了就停手一会儿再扫</summary>
    /// <param name="query">搜索词</param>
    public void SetQuery(string query)
    {
        string keyword = query.Trim();
        if (keyword == _query) return;

        _query = keyword;
        if (keyword.Length == 0) _isStarted = false;
        NotifyState();
        Rescan(TypingDebounce);
    }

    /// <summary>开扫：在这一侧全部会话的消息里搜当前的词</summary>
    [RelayCommand]
    public void Start()
    {
        if (_isStarted || _query.Length == 0) return;

        _isStarted = true;
        NotifyState();
        Rescan(TimeSpan.Zero);
    }

    /// <summary>要扫的会话整批换了（切了类型）：开着就按原词重扫</summary>
    public void NotifySessionsChanged()
    {
        if (IsActive) Rescan(TimeSpan.Zero);
    }

    /// <summary>会话被删了：摘掉它那一组结果（点下去已经无处可去），不为此重扫</summary>
    /// <param name="sessionId">会话标识</param>
    public void NotifySessionRemoved(string sessionId)
    {
        if (Results.FirstOrDefault(x => x.SessionId == sessionId) is not { } row) return;

        Results.Remove(row);
        RefreshHeader();
    }

    /// <summary>点一条摘要</summary>
    /// <param name="hit">那条摘要</param>
    [RelayCommand]
    public void Open(SessionContentHitRow hit) => OpenRequested?.Invoke(hit);

    /// <summary>停掉还没到点的扫描，作废还在后台的那次</summary>
    public void Dispose() => _runner.Dispose();

    private void NotifyState()
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(CanStart));
        OnPropertyChanged(nameof(StartText));
    }

    private void Rescan(TimeSpan delay)
    {
        if (!IsActive)
        {
            _runner.Cancel();
            IsSearching = false; //被作废的那次不会再来复位
            Results.Clear();
            RefreshHeader();
            return;
        }

        HeaderText = Loc.Text(LangKey.SessionContentSearchSearching);
        _runner.Schedule(delay, ScanAsync);
    }

    private async Task ScanAsync(CancellationToken token)
    {
        string query = _query;
        // 会话清单在 UI 线程上取:列表随时在增删、按更新时间重排
        IReadOnlyList<SessionListItem> sessions = _sessions();
        string[] ids = sessions.Select(x => x.SessionId).ToArray();
        IsSearching = true;
        try
        {
            List<SessionContentMatch> matches = await Task.Run(() => SessionContentSearch.Scan(ids,
                _readHistoryLines, query, SnippetsPerSession, default, token), token);
            if (token.IsCancellationRequested) return;

            Dictionary<string, SessionListItem> byId = sessions.ToDictionary(x => x.SessionId);
            Results.Clear();
            foreach (SessionContentMatch match in matches)
                Results.Add(new SessionContentResultRow(match, byId[match.SessionId], query));
            RefreshHeader();
        }
        catch (Exception e) when (e is not OperationCanceledException && !token.IsCancellationRequested)
        {
            // 单个会话读不了已在扫描里吞掉,走到这里是整次扫描出了意外:别让标题一直停在「正在搜索」
            Log.Error($"Content search failed: {e}");
            HeaderText = Loc.Text(LangKey.SessionContentSearchFailed);
        }
        finally
        {
            if (!token.IsCancellationRequested) IsSearching = false;
        }
    }

    private void RefreshHeader()
    {
        HeaderText = !IsActive ? string.Empty
            : Results.Count == 0 ? Loc.Text(LangKey.SessionContentSearchNoMatch)
            : Loc.Text(LangKey.SessionContentSearchHeader, Results.Count);
    }
}
