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
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation.SessionList;

/// <summary>
/// 会话列表：条目集合、选中、运行态、条目级事件的转发。
///
/// 普通对话与智能体共用同一份：内部维护<b>全量</b>条目（<see cref="_all"/>，实例常驻、跨类型复用），
/// 对外只暴露当前类型过滤后的 <see cref="Sessions"/>。切类型（左栏切换器）只是重排显示集合，
/// 不重建条目实例——这是\"切页签\"不卡顿的来源，全量清单同时是搜索与批量的底座。
///
/// 搜索与批量见同目录的 <c>SessionListModel.Search.cs</c> 与 <c>SessionListModel.Batch.cs</c>。
///
/// 原先两份代码（<c>ChatListViewModel</c> 与 <c>AgentPageData</c> 内联那一摊）
/// 的运行态刷新段甚至逐字相同，故并作一份。
/// </summary>
public partial class SessionListModel : ObservableObject, IDisposable
{
    private EConversationType _type;
    private readonly Action<Action> _post;
    private readonly IMessageService _messageService;
    private readonly Func<List<ChatSessionMeta>>? _source; //测试用的固定全量清单;生产为 null,从索引现取

    /// <summary>全量条目（含当前类型与另一类型），按最后更新时间倒序。条目实例只在这里创建</summary>
    private readonly List<SessionListItem> _all = new();

    /// <summary>每类型上次看的会话：切类型回来时接着看，不回空态（找不到才退首条）</summary>
    private readonly Dictionary<EConversationType, string> _lastSelectedByType = new();

    /// <summary>
    /// 各类型上次看的会话标识。页面据此保留会话实例，切类型回来直接复用，不走冷重载
    /// </summary>
    public IReadOnlyCollection<string> LastSelectedSessionIds => _lastSelectedByType.Values;

    private bool _suppressSelectionNotify;

    /// <summary>当前类型过滤后的显示集合（ListBox 绑这一份）</summary>
    public ObservableCollection<SessionListItem> Sessions { get; } = new();

    [ObservableProperty] private SessionListItem? _selectedSession;

    /// <summary>选中变了（<see cref="SelectWithoutNotifying"/> 造成的变化不抛）</summary>
    public event Action<SessionListItem?>? SelectionChanged;

    /// <summary>某个会话被就地改写（改名 / 清空历史）</summary>
    public event Action<SessionListItem>? Mutated;

    /// <summary>某个会话已被删除。此时若删的正是选中项，选中已被置空且未通知，由页面决定接着选谁</summary>
    public event Action<SessionListItem>? Removed;

    /// <param name="type">初始会话类型（左栏切换器决定）</param>
    /// <param name="post">回 UI 线程的方式；测试传同步执行</param>
    /// <param name="messageService">条目的确认弹窗；省略则从容器取</param>
    public SessionListModel(EConversationType type,
        Action<Action>? post = null, IMessageService? messageService = null)
        : this(type, null, post, messageService)
    {
    }

    /// <summary>
    /// 把全量清单来源换成固定数据（测试用）。<paramref name="source"/> 返回<b>全部</b>会话
    /// （不含子会话），类型过滤在模型内部做——与生产路径（<c>SessionManager.GetSessions</c>）同形状。
    /// 类型归路仍是 <c>CharacterKindRouting</c> 一个出口
    /// </summary>
    internal SessionListModel(EConversationType type,
        Func<List<ChatSessionMeta>>? source, Action<Action>? post, IMessageService? messageService)
    {
        _source = source;
        _type = type;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _messageService = messageService ?? App.Services.GetRequiredService<IMessageService>();

        Sync();

        // 主题切换后逐条刷新项目色:条目是复用的不是重建的(见 Sync 注释),绑定值不会自己重算
        if (Application.Current is { } app) app.ActualThemeVariantChanged += OnThemeVariantChanged;
        if (_source == null)
        {
            // 测试模式（_source 固定清单）不订阅全局会话事件：测试模型是自洽的固定清单，
            // 订阅只会让别的测试线程 SaveMeta 时把 Sync 驱动到本模型上，与模型自己的线程
            // 双写无锁的 _all / _lastSelectedByType（全量并行 flaky 的根源）。
            // 运行态事件仍订阅——运行态测试确实在用。
            SessionManager.Instance.OnSessionAdded += OnSessionAdded;
            SessionManager.Instance.OnSessionRemoved += OnSessionRemoved;
            SessionManager.Instance.OnSessionMetaUpdated += OnSessionMetaUpdated;
            SessionManager.Instance.OnSessionDraftChanged += OnSessionDraftChanged;
        }

        SessionManager.Instance.Running.StateChanged += OnRunStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged += OnRunStateChanged;
        Sessions.CollectionChanged += (_, _) =>
        {
            RefreshListChrome();
            RefreshCheckedState();
        };
    }

    //================= 同步 =================

    /// <summary>
    /// 把列表对到索引现在的样子：全量对帐（<see cref="_all"/>，按标识<b>复用</b>条目）+
    /// 按当前类型重建显示集合（<see cref="Sessions"/>，同样复用实例只做增删移）。
    ///
    /// 不用 Clear + 重填：那会经 ListBox 的双向绑定把选中抹成 null，于是要么丢选中、
    /// 要么得靠一个手写的抑制标志绕过去。复用条目则连带解决另两件事——
    /// 顺序（索引按最后更新时间倒序，说过话的会话要浮到顶部）与标题/时间戳的刷新。
    /// </summary>
    public void Sync()
    {
        SessionListItem? selected = SelectedSession;
        _suppressSelectionNotify = true;
        try
        {
            ReconcileAll();
            RebuildFiltered();
        }
        finally
        {
            RestoreSelection(selected);
            _suppressSelectionNotify = false;
        }

        // 再补一次:上面那次只兜住同步写回,而绑定也可能在本次调用返回之后才被打断
        _post(() => RestoreSelectionQuietly(selected));
    }

    /// <summary>把全量清单对到索引：删消失、补新增、接上新元数据、修顺序</summary>
    private void ReconcileAll()
    {
        List<ChatSessionMeta> metas = ListAllSessions();
        HashSet<string> wanted = new(metas.Count);
        foreach (ChatSessionMeta meta in metas) wanted.Add(meta.SessionId);

        // 先删再排:留着已消失的条目会让下标与目标顺序对不上
        for (int i = _all.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(_all[i].SessionId)) continue;
            Detach(_all[i]);
            ForgetMemory(_all[i].SessionId);
            _all.RemoveAt(i);
        }

        for (int i = 0; i < metas.Count; i++)
        {
            ChatSessionMeta meta = metas[i];
            int at = IndexOfAll(meta.SessionId);
            if (at < 0)
            {
                _all.Insert(i, Attach(new SessionListItem(meta, _messageService)));
                continue;
            }

            _all[at].UpdateMeta(meta);
            if (at != i) MoveAll(at, i);
        }
    }

    /// <summary>
    /// 按当前类型重建显示集合：期望顺序 = 全量中匹配类型<b>且匹配搜索词</b>的条目顺序，
    /// 增量对齐（删/插/移），不重建实例。类型切换只走这一条，因此切类型不碰全量对帐、非常快
    /// </summary>
    private void RebuildFiltered()
    {
        List<SessionListItem> wanted = _all.Where(x => BelongsHere(x.Meta) && MatchesSearch(x.Meta)).ToList();
        HashSet<SessionListItem> wantedSet = new(wanted);

        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            // 只从显示集合摘除，不 Detach——实例仍归 _all 管，切回类型还会再挂上
            if (!wantedSet.Contains(Sessions[i])) Sessions.RemoveAt(i);
        }

        for (int i = 0; i < wanted.Count; i++)
        {
            SessionListItem item = wanted[i];
            int at = Sessions.IndexOf(item);
            if (at < 0) { Sessions.Insert(i, item); continue; }
            if (at != i) Sessions.Move(at, i);
        }
    }

    /// <summary>把对帐前的选中放回去（见 <see cref="Sync"/> 的注释）</summary>
    private void RestoreSelection(SessionListItem? selected)
    {
        // 按全量找回：被搜索/类型滤掉的只是"当前不可见"，中间还在看它，选中要留着；
        // 真删掉的才置空（全量里也没有）
        SessionListItem? target = selected != null && IndexOfAll(selected.SessionId) >= 0 ? selected : null;
        if (!ReferenceEquals(SelectedSession, target)) SelectedSession = target;
    }

    /// <summary>延迟一帧的兜底，只补「选中被写回 null」这一种情形</summary>
    private void RestoreSelectionQuietly(SessionListItem? selected)
    {
        if (SelectedSession != null || selected == null) return;
        if (IndexOfAll(selected.SessionId) < 0) return;

        _suppressSelectionNotify = true;
        try
        {
            SelectedSession = selected;
        }
        finally
        {
            _suppressSelectionNotify = false;
        }
    }

    /// <summary>找出承载某会话的条目（只在当前显示集合里找；另一类型的会话本来就不该在这）</summary>
    public SessionListItem? Find(string sessionId)
    {
        int at = IndexOf(sessionId);
        return at < 0 ? null : Sessions[at];
    }

    /// <summary>
    /// 切换列表类型（左栏切换器调用）。换显示集合，并按「上次看的→首条→空」的口径
    /// <b>静默</b>选中：选中只是摆好，装载由页面显式驱动（单一路口，不经事件——
    /// 角色页直达那种「先换类型再指会话」的连续切换才不会装载两次）。
    /// </summary>
    /// <param name="type">目标类型</param>
    public void SwitchType(EConversationType type)
    {
        if (_type == type) return;
        _type = type;
        RebuildFiltered();
        SessionListItem? target = null;
        if (_lastSelectedByType.TryGetValue(type, out string? id)) target = Find(id);
        SelectWithoutNotifying(target ?? Sessions.FirstOrDefault());
    }

    /// <summary>
    /// 选中某个会话（角色页开聊直达 / 空态右栏点角色用）：对帐一次后选中并触发切换。
    /// 会话可能刚由 <c>StartNewSession</c> 入索引，<c>OnSessionAdded</c> 的刷新还没跑到，
    /// 这里同步补一次对帐（幂等）。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void SelectSession(string sessionId)
    {
        Sync();
        if (Find(sessionId) is { } item) SelectedSession = item;
    }

    /// <summary>
    /// 选中某条目但不抛 <see cref="SelectionChanged"/>。
    /// 用于「列表变了，选中要跟着对齐」这类场合——那不是用户的选择，不该触发加载
    /// </summary>
    /// <param name="item">条目；null 为不选</param>
    public void SelectWithoutNotifying(SessionListItem? item)
    {
        _suppressSelectionNotify = true;
        try
        {
            SelectedSession = item;
        }
        finally
        {
            _suppressSelectionNotify = false;
        }
    }

    /// <summary>
    /// 选中第一条（没有则不选）。删掉当前会话后「接着选谁」是各页自己的口径，故不内置
    /// </summary>
    public void SelectFirstOrNone() => SelectedSession = Sessions.FirstOrDefault();

    public void Dispose()
    {
        // 无条件退订:对从未订阅的委托 -= 是空操作,保持无条件以免将来订阅条件变化时漏退
        if (Application.Current is { } app) app.ActualThemeVariantChanged -= OnThemeVariantChanged;
        SessionManager.Instance.OnSessionAdded -= OnSessionAdded;
        SessionManager.Instance.OnSessionRemoved -= OnSessionRemoved;
        SessionManager.Instance.OnSessionMetaUpdated -= OnSessionMetaUpdated;
        SessionManager.Instance.OnSessionDraftChanged -= OnSessionDraftChanged;
        SessionManager.Instance.Running.StateChanged -= OnRunStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged -= OnRunStateChanged;
        foreach (SessionListItem item in _all) Detach(item);
    }

    //================= 内部 =================

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        // 无论静默与否都同步\"当前展示\"标记:SelectWithoutNotifying 也改了选中,
        // 页面壳据此判断当前会话,而 IsCurrent 是列表项的界面状态,不归 SelectionChanged 管.
        // 顺带记住每类型的上次选中:切类型回来时接着看（静默对齐也记，它反映的就是当前在看谁）
        UpdateCurrent(value);
        if (value != null) _lastSelectedByType[_type] = value.SessionId;
        if (_suppressSelectionNotify) return;
        SelectionChanged?.Invoke(value);
    }

    /// <summary>把每条的 <see cref="SessionListItem.IsCurrent"/> 对齐到当前选中</summary>
    private void UpdateCurrent(SessionListItem? value)
    {
        foreach (SessionListItem item in Sessions)
        {
            bool current = ReferenceEquals(item, value);
            if (item.IsCurrent != current) item.IsCurrent = current;
        }
    }

    private List<ChatSessionMeta> ListAllSessions()
    {
        if (_source != null) return _source();
        // 全量但排除子会话:左栏是跨会话导航,子会话是会话内的事（与旧 GetChatSessions/GetAgentSessions 同口径）
        return SessionManager.Instance.GetSessions().Where(x => !x.IsSubSession).ToList();
    }

    private bool BelongsHere(ChatSessionMeta meta)
    {
        ECharacterKind kind = SessionManager.KindOf(meta);
        return _type == EConversationType.Chat ? kind.IsChat() : kind.IsAgent();
    }

    private int IndexOf(string sessionId)
    {
        for (int i = 0; i < Sessions.Count; i++)
        {
            if (Sessions[i].SessionId == sessionId) return i;
        }

        return -1;
    }

    private int IndexOfAll(string sessionId)
    {
        for (int i = 0; i < _all.Count; i++)
        {
            if (_all[i].SessionId == sessionId) return i;
        }

        return -1;
    }

    private void MoveAll(int from, int to)
    {
        SessionListItem item = _all[from];
        _all.RemoveAt(from);
        _all.Insert(to, item);
    }

    private void OnSessionAdded(ChatSession session)
    {
        // 全量对帐自然处理归属：新会话不是本类型就只是不进显示集合，不会出现在别家
        _post(() => Sync());
    }

    private void OnSessionRemoved(ChatSession session) => _post(() => Remove(session.SessionId));

    /// <summary>
    /// 某会话的元数据刷新了。全量对帐而不是只更新那一条——一次落盘同时改了两件事：
    /// 该条目的时间戳，以及它在按时间倒序的清单里的位置，而后者是相对全体的
    /// </summary>
    private void OnSessionMetaUpdated(ChatSession session)
    {
        // 每轮落盘都到这里(可能在后台线程),而条目是界面绑定的
        _post(Sync);
    }

    private void OnRunStateChanged(string sessionId) =>
        // 可能来自后台线程(无头执行),而条目是界面绑定的
        _post(() => Find(sessionId)?.RefreshRunState());

    /// <summary>主题切换后重取每个条目的项目色。条目复用意味着 Brush 不会自动重算</summary>
    private void OnThemeVariantChanged(object? sender, EventArgs e)
    {
        foreach (SessionListItem item in _all) item.RefreshWorkspaceColor();
    }

    /// <summary>
    /// 某会话的草稿状态变了。只更新那一条的小标记,不做全量重排——\n
    /// 草稿落盘走的是 touchUpdatedAt=false 分支,UpdatedAt 没动,列表顺序不该变
    /// </summary>
    private void OnSessionDraftChanged(ChatSession session, ChatSessionMeta meta) =>
        _post(() => Find(session.SessionId)?.UpdateMeta(meta));

    /// <summary>
    /// 摘掉一个条目。两条路径都会到这里——全局的会话删除通知，以及条目自己的删除命令
    /// （<c>Delete</c> 在本体加载不出来时不抛全局通知，那种会话只有后一条路径管得到）。
    /// 后到的那一次是空操作。
    /// </summary>
    private void Remove(string sessionId)
    {
        int atAll = IndexOfAll(sessionId);
        if (atAll < 0) return;

        SessionListItem item = _all[atAll];
        Detach(item);
        _all.RemoveAt(atAll);
        int atShown = Sessions.IndexOf(item);
        if (atShown >= 0) Sessions.RemoveAt(atShown);
        ForgetMemory(sessionId);
        // 不替页面决定接着选谁:各页口径不同
        if (ReferenceEquals(SelectedSession, item)) SelectWithoutNotifying(null);
        Removed?.Invoke(item);
    }

    /// <summary>忘掉对某会话的记忆（删除/消失时用）：切类型回来不再误找，直接退首条</summary>
    private void ForgetMemory(string sessionId)
    {
        foreach (EConversationType type in _lastSelectedByType.Keys.ToList())
        {
            if (_lastSelectedByType[type] == sessionId) _lastSelectedByType.Remove(type);
        }
    }

    private SessionListItem Attach(SessionListItem item)
    {
        item.Mutated += OnItemMutated;
        item.Deleted += OnItemDeleted;
        item.PropertyChanged += OnItemPropertyChanged;
        return item;
    }

    private void Detach(SessionListItem item)
    {
        item.Mutated -= OnItemMutated;
        item.Deleted -= OnItemDeleted;
        item.PropertyChanged -= OnItemPropertyChanged;
    }

    private void OnItemMutated(SessionListItem item) => Mutated?.Invoke(item);

    private void OnItemDeleted(SessionListItem item) => Remove(item.SessionId);
}