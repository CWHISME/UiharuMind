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
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话列表归属哪一页。判定一律经 <c>CharacterKindRouting</c>——
/// 调用方不传裸谓词，也就写不出 <c>Kind == Roleplay</c> 那种四档之后会漏掉工具人的判据
/// </summary>
public enum ESessionListScope
{
    /// <summary>聊天工作台：扮演与工具人两档</summary>
    Chat,

    /// <summary>智能体页</summary>
    Agent,
}

/// <summary>
/// 一页的会话列表：条目集合、选中、运行态、条目级事件的转发。
/// 聊天页与智能体页共用这一份。
///
/// 两页的差异只有构造参数里那两个，其余行为完全相同——原先是两份代码
/// （<c>ChatListViewModel</c> 与 <c>AgentPageData</c> 内联那一摊），
/// 运行态刷新那段甚至逐字相同。
/// </summary>
public partial class SessionListModel : ObservableObject, IDisposable
{
    private readonly ESessionListScope _scope;
    private readonly bool _selectNewSessions;
    private readonly Action<Action> _post;
    private readonly IMessageService _messageService;
    private readonly Func<List<ChatSessionMeta>>? _source; //测试用的固定清单;生产为 null,按档位现取

    private bool _suppressSelectionNotify;

    public ObservableCollection<SessionListItem> Sessions { get; } = new();

    [ObservableProperty] private SessionListItem? _selectedSession;

    /// <summary>选中变了（<see cref="SelectWithoutNotifying"/> 造成的变化不抛）</summary>
    public event Action<SessionListItem?>? SelectionChanged;

    /// <summary>某个会话被就地改写（改名 / 清空历史）</summary>
    public event Action<SessionListItem>? Mutated;

    /// <summary>某个会话已被删除。此时若删的正是选中项，选中已被置空且未通知，由页面决定接着选谁</summary>
    public event Action<SessionListItem>? Removed;

    /// <param name="scope">本列表归哪一页</param>
    /// <param name="selectNewSessions">
    /// 新会话进列表时是否自动选中。
    ///
    /// <b>刻意不从 <paramref name="scope"/> 推导</b>：它取决于会话何时创建，与哪一页无关。
    /// 聊天页是<b>急建</b>（点新建按钮就入索引，角色的开场白当场写进历史，所以该切过去）；
    /// 智能体页是<b>懒建</b>（首轮发送时才入索引，那条通知落在正在跑的一轮中间，
    /// 改选中会触发重载、把正在流的回复拦腰截断）。
    /// </param>
    /// <param name="post">回 UI 线程的方式；测试传同步执行</param>
    /// <param name="messageService">条目的确认弹窗；省略则从容器取</param>
    public SessionListModel(ESessionListScope scope, bool selectNewSessions,
        Action<Action>? post = null, IMessageService? messageService = null)
        : this(scope, selectNewSessions, null, post, messageService)
    {
    }

    /// <summary>
    /// 把清单来源换成固定数据（测试用）。<paramref name="scope"/> 仍决定新会话的档位归属判定，
    /// 那一条不可替换——归路只有 <c>CharacterKindRouting</c> 一个出口
    /// </summary>
    internal SessionListModel(ESessionListScope scope, bool selectNewSessions,
        Func<List<ChatSessionMeta>>? source, Action<Action>? post, IMessageService? messageService)
    {
        _source = source;
        _scope = scope;
        _selectNewSessions = selectNewSessions;
        _post = post ?? (action => Dispatcher.UIThread.Post(action));
        _messageService = messageService ?? App.Services.GetRequiredService<IMessageService>();

        Sync();

        SessionManager.Instance.OnSessionAdded += OnSessionAdded;
        SessionManager.Instance.OnSessionRemoved += OnSessionRemoved;
        SessionManager.Instance.OnSessionMetaUpdated += OnSessionMetaUpdated;
        SessionManager.Instance.Running.StateChanged += OnRunStateChanged;
    }

    //================= 同步 =================

    /// <summary>
    /// 把列表对到索引现在的样子：按标识<b>复用</b>已有条目、接上新元数据、补新增、删消失、修顺序。
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
            Reconcile();
        }
        finally
        {
            RestoreSelection(selected);
            _suppressSelectionNotify = false;
        }

        // 再补一次:上面那次只兜住同步写回,而绑定也可能在本次调用返回之后才被打断
        _post(() => RestoreSelectionQuietly(selected));
    }

    /// <summary>
    /// 把列表对到目标清单：删消失、补新增、接上新元数据、修顺序
    /// </summary>
    private void Reconcile()
    {
        List<ChatSessionMeta> metas = ListSessions();
        HashSet<string> wanted = new(metas.Count);
        foreach (ChatSessionMeta meta in metas) wanted.Add(meta.SessionId);

        // 先删再排:留着已消失的条目会让下标与目标顺序对不上
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            if (wanted.Contains(Sessions[i].SessionId)) continue;
            Detach(Sessions[i]);
            Sessions.RemoveAt(i);
        }

        for (int i = 0; i < metas.Count; i++)
        {
            ChatSessionMeta meta = metas[i];
            int at = IndexOf(meta.SessionId);
            if (at < 0)
            {
                Sessions.Insert(i, Attach(new SessionListItem(meta, _messageService)));
                continue;
            }

            Sessions[at].UpdateMeta(meta);
            if (at != i) Sessions.Move(at, i);
        }
    }

    /// <summary>
    /// 把对帐前的选中放回去。
    ///
    /// 对帐会移动与增删条目，<c>ListBox.SelectedItem</c> 的双向绑定会因此被打断并写回
    /// <c>null</c>——那不是用户的选择，一旦冒成 <see cref="SelectionChanged"/>
    /// 就会把正在看的会话卸掉、对话区清空（说过话的会话浮到顶部时当场可见）。
    /// 条目还在就原样放回；真消失了才置空，此时页面另有 <see cref="Removed"/> 可依据。
    /// </summary>
    /// <param name="selected">对帐前的选中项</param>
    private void RestoreSelection(SessionListItem? selected)
    {
        SessionListItem? target = selected != null && IndexOf(selected.SessionId) >= 0 ? selected : null;
        if (!ReferenceEquals(SelectedSession, target)) SelectedSession = target;
    }

    /// <summary>
    /// 延迟一帧的兜底，只补「选中被写回 null」这一种情形。
    /// 不能无条件恢复：用户在这期间主动切了会话的话，那会把他拽回旧会话
    /// </summary>
    /// <param name="selected">对帐前的选中项</param>
    private void RestoreSelectionQuietly(SessionListItem? selected)
    {
        if (SelectedSession != null || selected == null) return;
        if (IndexOf(selected.SessionId) < 0) return;

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

    /// <summary>
    /// 找出承载某会话的条目
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>条目；不在本列表则为 null</returns>
    public SessionListItem? Find(string sessionId)
    {
        int at = IndexOf(sessionId);
        return at < 0 ? null : Sessions[at];
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
        SessionManager.Instance.OnSessionAdded -= OnSessionAdded;
        SessionManager.Instance.OnSessionRemoved -= OnSessionRemoved;
        SessionManager.Instance.OnSessionMetaUpdated -= OnSessionMetaUpdated;
        SessionManager.Instance.Running.StateChanged -= OnRunStateChanged;
        foreach (SessionListItem item in Sessions) Detach(item);
    }

    //================= 内部 =================

    partial void OnSelectedSessionChanged(SessionListItem? value)
    {
        if (_suppressSelectionNotify) return;
        SelectionChanged?.Invoke(value);
    }

    private List<ChatSessionMeta> ListSessions()
    {
        if (_source != null) return _source();
        return _scope == ESessionListScope.Chat
            ? SessionManager.Instance.GetChatSessions()
            : SessionManager.Instance.GetAgentSessions();
    }

    private bool BelongsHere(ChatSession session)
    {
        ECharacterKind kind = session.CharacterData.Kind;
        return _scope == ESessionListScope.Chat ? kind.IsChat() : kind.IsAgent();
    }

    private int IndexOf(string sessionId)
    {
        for (int i = 0; i < Sessions.Count; i++)
        {
            if (Sessions[i].SessionId == sessionId) return i;
        }

        return -1;
    }

    private void OnSessionAdded(ChatSession session)
    {
        if (!BelongsHere(session)) return;
        // 可能来自后台线程(调度器新建会话),也可能就在 UI 线程上(新建按钮、首轮发送)
        _post(() =>
        {
            Sync();
            if (_selectNewSessions && Find(session.SessionId) is { } item) SelectedSession = item;
        });
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

    /// <summary>
    /// 摘掉一个条目。两条路径都会到这里——全局的会话删除通知，以及条目自己的删除命令
    /// （<c>Delete</c> 在本体加载不出来时不抛全局通知，那种会话只有后一条路径管得到）。
    /// 后到的那一次是空操作。
    /// </summary>
    private void Remove(string sessionId)
    {
        int at = IndexOf(sessionId);
        if (at < 0) return;

        SessionListItem item = Sessions[at];
        Detach(item);
        Sessions.RemoveAt(at);
        // 不替页面决定接着选谁:两页口径不同(聊天页选下一条,智能体页回空态)
        if (ReferenceEquals(SelectedSession, item)) SelectWithoutNotifying(null);
        Removed?.Invoke(item);
    }

    private SessionListItem Attach(SessionListItem item)
    {
        item.Mutated += OnItemMutated;
        item.Deleted += OnItemDeleted;
        return item;
    }

    private void Detach(SessionListItem item)
    {
        item.Mutated -= OnItemMutated;
        item.Deleted -= OnItemDeleted;
    }

    private void OnItemMutated(SessionListItem item) => Mutated?.Invoke(item);

    private void OnItemDeleted(SessionListItem item) => Remove(item.SessionId);
}
