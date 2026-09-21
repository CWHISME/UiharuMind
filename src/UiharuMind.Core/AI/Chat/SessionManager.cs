/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.AI.Execution.ToolCall;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话的索引与持久化。取代了 ChatManager(启动时全量加载所有会话)与
/// AgentSessionIndex(agent 会话另一套索引)两套实现：
/// 角色对话与 agent 对话现在是同一种会话，同一个索引，同一套文件布局。
///
/// 布局：
///   SessionData/index.json              —— 元数据索引，启动只读这个
///   SessionData/{sessionId}.json        —— 会话本体，按需加载
///   SessionData/{sessionId}.agentstate.json —— 框架附加状态(todos/mode/审批)，可丢弃
/// </summary>
public class SessionManager : Singleton<SessionManager>, IInitialize
{
    private const string IndexFileName = "index.json";
    private const string AgentStateSuffix = ".agentstate.json";
    private const string MetaSuffix = ".meta.json"; //会话头(小,原子重写)
    private const string HistorySuffix = ".history.jsonl"; //历史(一行一条消息,追加式)

    /// <summary>会话新增</summary>
    public event Action<ChatSession>? OnSessionAdded;

    /// <summary>会话删除</summary>
    public event Action<ChatSession>? OnSessionRemoved;

    /// <summary>
    /// 会话元数据已刷新（标题、描述、最后更新时间、消息数）。
    ///
    /// 每次 <see cref="SaveMeta"/> 都抛——它同时是「<see cref="ChatSessionMeta.UpdatedAt"/> 变了」的
    /// 唯一时机，而排序按该字段倒序，所以列表既靠它重排也靠它刷新时间。
    /// 少了这个通知，说过话的会话不会浮到顶部、时间也停在开页那一刻。
    /// </summary>
    public event Action<ChatSession>? OnSessionMetaUpdated;

    /// <summary>
    /// 会话的输入草稿状态变了（有/无未发送输入）。
    ///
    /// 与 <see cref="OnSessionMetaUpdated"/> 分开：草稿落盘走 <see cref="SaveMeta"/> 的
    /// <c>touchUpdatedAt=false</c> 分支（不重排），但列表项上的小标记仍要随之刷新。
    /// 只在草稿状态<b>翻转</b>时抛，避免每次打字/落盘都搅动列表。
    /// 参数携带<b>落盘后的新元数据</b>：列表项要换引用才能读到新的草稿标记，
    /// 只刷那一条不必做全量 Sync。
    /// </summary>
    public event Action<ChatSession, ChatSessionMeta>? OnSessionDraftChanged;

    /// <summary>
    /// 运行态登记处：界面的运行指示器、「跑时禁用删除」都读它，
    /// 界面轮次与无头轮次都往它上面登记
    /// </summary>
    public SessionRunRegistry Running { get; } = new();

    // 两张表与索引落盘共用一把锁:子代理并发派出、定时任务无头跑着时用户新建会话,
    // 都会从非 UI 线程进来。锁内只做内存操作与索引写盘,事件一律在锁外触发,免得监听方回调重入
    private readonly object _locker = new();
    private readonly Dictionary<string, ChatSessionMeta> _metas = new();

    // 已加载的本体(含临时会话)。临时会话只存在于此,不落盘也不进 _metas。
    // 本体一旦加载就<b>不换实例也不摘掉</b>——持有它的人认的是这一个实例。
    // 按会话长度增长的只有历史,那一份由驻留策略管(见 SessionResidencyPolicy)
    private readonly Dictionary<string, ChatSession> _loaded = new();

    private readonly ConcurrentDictionary<string, DateTime> _resident = new(); //历史仍在内存的会话及其最后访问时刻;不共用 _locker,卸载重载回调可能撞进任何持锁代码
    private readonly ConcurrentDictionary<string, int> _pins = new(); //被钉住的会话(界面壳挂着、条目还指着消息实例),钉住期间不卸历史

    /// <summary>已加载的会话本体数（含历史已卸载的那些）。诊断用</summary>
    public int LoadedSessionCount
    {
        get { lock (_locker) return _loaded.Count; }
    }

    /// <summary>历史仍留在内存里的会话数。诊断用，与驻留上限对照着看</summary>
    public int ResidentHistoryCount => _resident.Count;

    public void OnInitialize()
    {
        lock (_locker)
        {
            _metas.Clear();
            _loaded.Clear();

            List<ChatSessionMeta>? index =
                SaveUtility.Load<List<ChatSessionMeta>>(GetIndexPath(), SessionJsonOptions.Default);
            if (index == null)
            {
                // 索引缺失或损坏:本体文件才是权威,扫目录重建
                RebuildIndex();
                return;
            }

            foreach (ChatSessionMeta meta in index)
            {
                if (!string.IsNullOrEmpty(meta.SessionId)) _metas[meta.SessionId] = meta;
            }
        }
    }

    /// <summary>
    /// 全部会话元数据，按最后更新时间倒序
    /// </summary>
    /// <returns>元数据列表</returns>
    public List<ChatSessionMeta> GetSessions()
    {
        lock (_locker) return _metas.Values.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    /// <summary>
    /// 聊天页的会话（扮演与工具人两档），按最后更新时间倒序。
    /// 归类由角色实时派生而非存进元数据——角色的档位改变时会话随之换页，不会留下过期副本。
    /// </summary>
    /// <returns>元数据列表</returns>
    public List<ChatSessionMeta> GetChatSessions() => GetSessions(x => KindOf(x).IsChat());

    /// <summary>
    /// 智能体页的会话，按最后更新时间倒序
    /// </summary>
    /// <returns>元数据列表</returns>
    public List<ChatSessionMeta> GetAgentSessions() => GetSessions(x => KindOf(x).IsAgent());

    // 刻意不提供"传一个档位"的重载:那个形状邀请调用方写 GetSessions(Roleplay),
    // 四档之后工具人的会话就会两页都不显示(实机踩过)。分区只有上面这两个出口
    private List<ChatSessionMeta> GetSessions(Func<ChatSessionMeta, bool> predicate)
    {
        lock (_locker)
        {
            return _metas.Values
                // 子会话不进左栏:左栏是跨会话导航,而子会话是会话内的事。
                // 它仍然在索引里(右栏「子代理」面板按 ParentSessionId 取用),只是不在这两个出口露面
                .Where(x => !x.IsSubSession)
                .Where(predicate)
                .OrderByDescending(x => x.UpdatedAt)
                .ToList();
        }
    }

    /// <summary>
    /// 取会话所属角色的种类
    /// </summary>
    /// <param name="meta">会话元数据</param>
    /// <returns>角色种类；角色已被删除时按对话角色处理</returns>
    public static ECharacterKind KindOf(ChatSessionMeta meta)
    {
        return CharacterManager.Instance.GetCharacterData(meta.CharacterId).Kind;
    }

    /// <summary>
    /// 取某个会话派出去的全部子会话，按最后更新时间倒序。
    /// 右栏「子代理」面板的数据源——无需新存储，索引里本来就有
    /// </summary>
    /// <param name="parentSessionId">派活者的会话标识</param>
    /// <returns>子会话元数据列表</returns>
    public List<ChatSessionMeta> GetSubSessions(string? parentSessionId)
    {
        if (string.IsNullOrEmpty(parentSessionId)) return [];
        lock (_locker)
        {
            return _metas.Values
                .Where(x => string.Equals(x.ParentSessionId, parentSessionId, StringComparison.Ordinal))
                .OrderByDescending(x => x.UpdatedAt)
                .ToList();
        }
    }

    /// <summary>
    /// 取元数据
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>元数据；不存在为 null</returns>
    public ChatSessionMeta? GetMeta(string sessionId)
    {
        lock (_locker) return _metas.GetValueOrDefault(sessionId);
    }

    /// <summary>
    /// 按需加载会话本体（含内存中的临时会话），结果会被缓存
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话；文件缺失或损坏为 null</returns>
    public ChatSession? Load(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;

        ChatSession? loaded = ResolveLoaded(sessionId);
        // 命中也走一遍:超出上限的那几个未必赶得上下一次未命中,而没超限时这一趟几乎不花钱。
        // 排在锁外:卸载要写盘、要放执行者
        UnloadColdHistories();
        return loaded;
    }

    /// <summary>
    /// 取本体，缓存没有就读盘。<b>整段在锁内</b>：两个线程同时首次加载同一会话，
    /// 否则会各持一份本体，历史被写坏。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话；文件缺失或损坏为 null</returns>
    private ChatSession? ResolveLoaded(string sessionId)
    {
        lock (_locker)
        {
            if (_loaded.TryGetValue(sessionId, out ChatSession? cached))
            {
                Touch(sessionId);
                return cached;
            }

            ChatSession? session =
                SaveUtility.Load<ChatSession>(GetMetaPath(sessionId), SessionJsonOptions.Default);
            if (session == null)
            {
                Log.Warning($"Load chat session '{sessionId}' failed.");
                return null;
            }

            session.SessionId = sessionId;
            TrackHistory(session);
            session.History = LoadHistory(sessionId);
            // 进程级中断(崩溃/强杀)时当场补的代码跑不到,孤儿 tool_call 留在盘上——
            // 严格的服务端下一条请求直接 400,这个会话从此发不出话。读取时修:下次打开就有代码可跑了。
            // 只补末尾那一轮(硬杀只会留下末尾孤儿),中间的历史遗留孤儿不碰(追加到末尾会打乱配对顺序);
            // 补写幂等,已配对的不动,无孤儿时这里零开销
            ToolCallCancellation.CloseUnansweredAtTail(session);
            _loaded[sessionId] = session;
            return session;
        }
    }

    /// <summary>
    /// 钉住一个会话：钉住期间它的历史不会被卸掉。
    ///
    /// 谁该钉：<b>持有历史里那些 <see cref="ChatMessage"/> 实例的人</b>。界面壳是典型——
    /// 每个气泡都指着历史里的某一条，历史一换实例，编辑/删除/分叉/重试全部静默失效。
    /// 短促的读写不必钉，驻留策略的冷却时间已经把那条竞态排除了
    /// （见 <see cref="SessionResidencyPolicy.MinIdleBeforeUnload"/>）。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>解钉句柄；标识为空时是个空操作句柄</returns>
    public IDisposable Pin(string? sessionId) => new PinScope(this, sessionId);

    /// <summary>记一次访问。卸载只挑最冷的那几个，这里就是「冷」的判据</summary>
    private void Touch(string sessionId) => _resident[sessionId] = DateTime.UtcNow;

    /// <summary>
    /// 交代这个会话的历史怎么卸、怎么取回来。<b>只给落盘过的会话</b>——
    /// 临时会话只存在于内存，卸了就真没了。
    /// </summary>
    private void TrackHistory(ChatSession session)
    {
        string sessionId = session.SessionId;
        session.SetHistoryReload(() =>
        {
            Touch(sessionId);
            return LoadHistory(sessionId);
        });
        Touch(sessionId);
    }

    /// <summary>
    /// 把超出驻留上限的冷会话历史卸掉，连同它们的执行者一起放掉。
    ///
    /// 卸之前先把历史落一次盘：内存里那份才是权威，而「有没有人改过它还没保存」
    /// 在这里判不出来。多写一次几毫秒，换的是「卸载永远不会丢东西」。
    /// </summary>
    private void UnloadColdHistories()
    {
        IReadOnlyList<string> doomed =
            SessionResidencyPolicy.SelectForUnload(_resident, CanUnloadHistory, DateTime.UtcNow);

        foreach (string sessionId in doomed)
        {
            ChatSession? session;
            lock (_locker) _loaded.TryGetValue(sessionId, out session);
            if (session == null)
            {
                _resident.TryRemove(sessionId, out _);
                continue;
            }

            int count = session.History.Count;
            // 空历史一律不写:会话文件读坏时 LoadHistory 也会给出空列表,
            // 这时候回写等于拿一次读取失败把盘上那份真历史抹了
            if (count > 0)
            {
                SaveUtility.SaveText(GetHistoryPath(sessionId), HistoryJsonl.SerializeLines(session.History));
            }

            if (!session.UnloadHistory()) continue;

            _resident.TryRemove(sessionId, out _);
            // 执行者一起放掉:装配快照、框架会话状态与 MCP 租约都挂在它身上,
            // 留着一个冷会话的执行者没有意义,下次用到时惰性重建
            DisposeRunner(session);
            Log.Debug($"Unloaded history of cold session '{sessionId}' ({count} messages).");
        }
    }

    /// <summary>这个会话此刻卸得动吗</summary>
    private bool CanUnloadHistory(string sessionId)
    {
        if (_pins.ContainsKey(sessionId)) return false;

        ChatSession? session;
        lock (_locker) _loaded.TryGetValue(sessionId, out session);
        if (session == null || session.IsTransient) return false;

        // 在跑(含卡在审批上)、名下还有没交回的后台子代理:历史随时会被写,卸了就是在它脚下抽地板
        if (Running.IsBusy(sessionId)) return false;
        if (BackgroundSubAgentDispatcher.HasPendingWork(sessionId)) return false;
        return !session.BackgroundReportPending;
    }

    /// <summary>解钉句柄。重入计数：同一个会话可以被多个壳同时钉住</summary>
    private sealed class PinScope : IDisposable
    {
        private readonly SessionManager _owner;
        private readonly string? _sessionId;
        private bool _released;

        public PinScope(SessionManager owner, string? sessionId)
        {
            _owner = owner;
            _sessionId = sessionId;
            if (!string.IsNullOrEmpty(sessionId))
            {
                owner._pins.AddOrUpdate(sessionId, 1, (_, count) => count + 1);
            }
        }

        public void Dispose()
        {
            if (_released || string.IsNullOrEmpty(_sessionId)) return;
            _released = true;
            _owner._pins.AddOrUpdate(_sessionId, 0, (_, count) => count - 1);
            _owner._pins.TryRemove(new KeyValuePair<string, int>(_sessionId, 0));
        }
    }

    private static List<ChatMessage> LoadHistory(string sessionId)
    {
        string path = GetHistoryPath(sessionId);
        if (!File.Exists(path)) return [];
        try
        {
            return HistoryJsonl.Parse(File.ReadLines(path));
        }
        catch (Exception e)
        {
            Log.Warning($"Load history of '{sessionId}' failed: {e.Message}");
            return [];
        }
    }

    /// <summary>
    /// 新建并持久化一个会话
    /// </summary>
    /// <param name="characterData">所属角色</param>
    /// <returns>会话</returns>
    public ChatSession StartNewSession(CharacterData characterData)
    {
        ChatSession session = new(characterData.CharacterName, characterData);
        Add(session);
        return session;
    }

    /// <summary>
    /// 新建一个临时会话：不落盘、不进索引、不出现在列表，仅在内存中按标识可解析。
    /// 快捷翻译/解释等一次性调用用它，用户选择保留时再 <see cref="ChatSession.Persist"/>。
    /// </summary>
    /// <param name="characterId">所属角色标识</param>
    /// <param name="arguments">模板参数</param>
    /// <returns>临时会话</returns>
    public ChatSession CreateTransientSession(string characterId, Dictionary<string, object?>? arguments = null)
    {
        CharacterData character = CharacterManager.Instance.GetCharacterData(characterId);
        ChatSession session = new()
        {
            CharacterId = character.CharacterId,
            Title = character.CharacterName,
            Description = character.Description,
            IsTransient = true,
        };
        if (arguments != null)
        {
            foreach ((string key, object? value) in arguments) session.CustomParams[key] = value;
        }

        lock (_locker) _loaded[session.SessionId] = session;
        return session;
    }

    /// <summary>
    /// 把会话纳入索引并落盘
    /// </summary>
    /// <param name="session">会话</param>
    public void Add(ChatSession session)
    {
        session.IsTransient = false;
        lock (_locker) _loaded[session.SessionId] = session;
        TrackHistory(session); //这一刻起它就是落盘会话,冷下来之后照样让位
        Save(session);
        OnSessionAdded?.Invoke(session);
    }

    /// <summary>
    /// 落盘并刷新索引。临时会话为空操作。
    /// </summary>
    /// <param name="session">会话</param>


    public void Save(ChatSession session)
    {
        if (session.IsTransient) return;
        SaveUtility.SaveText(GetHistoryPath(session.SessionId), HistoryJsonl.SerializeLines(session.History));
        SaveMeta(session);
    }

    /// <summary>
    /// 只保存会话头与索引,不动历史文件(标题/参数/统计等头字段变更用)
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="touchUpdatedAt">是否刷新 UpdatedAt 并通知列表重排。
    /// 草稿这类高频、低价值的旁置状态落盘时应传 false,避免打字就搅动列表排序</param>
    public void SaveMeta(ChatSession session, bool touchUpdatedAt = true)
    {
        if (session.IsTransient) return;

        if (touchUpdatedAt) session.UpdatedAt = DateTimeOffset.Now;
        // 会话头冗余保存同一份元数据,索引损坏时可据此重建
        SaveUtility.Save(GetMetaPath(session.SessionId), session, SessionJsonOptions.Default);

        ChatSessionMeta meta = session.ToMeta();
        bool draftChanged;
        lock (_locker)
        {
            _loaded[session.SessionId] = session;
            draftChanged = _metas.TryGetValue(session.SessionId, out ChatSessionMeta? prev)
                && prev.HasComposerDraft != meta.HasComposerDraft;
            _metas[session.SessionId] = meta;
            SaveIndex();
        }

        if (!touchUpdatedAt && draftChanged) OnSessionDraftChanged?.Invoke(session, meta);
        if (touchUpdatedAt) OnSessionMetaUpdated?.Invoke(session);
    }

    /// <summary>
    /// 追加保存:把 History 自 fromIndex 起的新消息追加到历史文件,并刷新会话头。
    /// 常规轮次落盘走这里——追加成本与会话长度无关;
    /// 进程中断最坏留下残缺尾行,读取端逐行容错,旧数据不受影响。
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="fromIndex">新消息起始下标</param>
    public void Append(ChatSession session, int fromIndex)
    {
        if (session.IsTransient) return;

        int from = Math.Clamp(fromIndex, 0, session.History.Count);
        string path = GetHistoryPath(session.SessionId);
        if (from == 0 || !File.Exists(path))
        {
            // 从头写或文件缺失:退化为全量,保证文件与内存一致
            Save(session);
            return;
        }

        try
        {
            File.AppendAllText(path, HistoryJsonl.SerializeLines(session.History.Skip(from)));
        }
        catch (Exception e)
        {
            Log.Error($"Append history of '{session.SessionId}' failed: {e.Message}");
        }

        SaveMeta(session);
    }

    /// <summary>
    /// 复制一个会话（新标识、标题加后缀）
    /// </summary>
    /// <param name="session">源会话</param>
    /// <param name="titleSuffix">标题后缀</param>
    /// <returns>新会话</returns>
    public ChatSession Copy(ChatSession session, string titleSuffix = "_Copy")
    {
        ChatSession copy = DeepCopy(session);
        copy.SessionId = Guid.NewGuid().ToString("N");
        copy.Title += titleSuffix;
        copy.CreatedAt = DateTimeOffset.Now;
        // 附件文件仍归原会话所有:两边都登记会导致删除任一方时打断另一方
        copy.OwnedAttachmentFiles.Clear();
        Add(copy);
        return copy;
    }

    /// <summary>
    /// 深拷贝一个会话（不入索引）
    /// </summary>
    /// <param name="session">源会话</param>
    /// <returns>副本</returns>
    public ChatSession DeepCopy(ChatSession session)
    {
        string json = JsonSerializer.Serialize(session, SessionJsonOptions.Default);
        ChatSession copy = JsonSerializer.Deserialize<ChatSession>(json, SessionJsonOptions.Default)!;
        // History 不随会话头序列化(JsonIgnore),单独往返一次拿到完全独立的副本
        string history = JsonSerializer.Serialize(session.History, SessionJsonOptions.Default);
        copy.History = JsonSerializer.Deserialize<List<ChatMessage>>(history, SessionJsonOptions.Default) ?? [];
        return copy;
    }

    /// <summary>
    /// 删除会话及其全部文件
    /// </summary>
    /// <param name="session">会话</param>
    public void Delete(ChatSession session)
    {
        Delete(session.SessionId);
    }

    /// <summary>
    /// 删除会话及其全部文件
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void Delete(string sessionId)
    {
        // 级联删掉它派出去的子会话:子会话的入口全都挂在派活者身上(卡片与右栏面板),
        // 派活者没了它们就再也打不开,留在盘上只是孤儿。先收集再删——
        // 递归调用会改 _metas,边遍历边删会抛
        foreach (ChatSessionMeta child in GetSubSessions(sessionId))
        {
            Delete(child.SessionId);
        }

        // 附件路径记在本体里,所以要在删文件之前把它读出来
        ChatSession? session = Load(sessionId);
        DeleteOwnedAttachments(session);
        DisposeRunner(session);

        SaveUtility.Delete(GetMetaPath(sessionId));
        SaveUtility.Delete(GetHistoryPath(sessionId));
        SaveUtility.Delete(GetBodyPath(sessionId)); //旧单文件格式残留
        SaveUtility.Delete(GetAgentStatePath(sessionId));
        AgentOutputLayout.DeleteAll(sessionId); //agent 画的图与导出的数据,按 id 后缀通配

        lock (_locker)
        {
            _loaded.Remove(sessionId);
            _resident.TryRemove(sessionId, out _);
            if (_metas.Remove(sessionId)) SaveIndex();
        }

        if (session != null) OnSessionRemoved?.Invoke(session);
    }

    /// <summary>
    /// 清理本会话自己落盘的附件。只删应用创建的文件，用户从磁盘选中的原始文件不在此列。
    /// </summary>
    private static void DeleteOwnedAttachments(ChatSession? session)
    {
        if (session == null) return;
        foreach (string path in session.OwnedAttachmentFiles)
        {
            SaveUtility.Delete(path);
        }
    }

    /// <summary>
    /// 把会话从内存缓存卸载并释放其执行者。临时会话用完（快捷窗口关闭且未保留）时调用；
    /// 已落盘的会话卸载后可随时经 <see cref="Load"/> 重新加载。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void Release(string sessionId)
    {
        ChatSession? session;
        lock (_locker)
        {
            if (!_loaded.Remove(sessionId, out session)) return;
        }

        _resident.TryRemove(sessionId, out _);

        DisposeRunner(session);
    }

    /// <summary>
    /// 释放全部已加载会话的执行者（应用退出时调用，尽力而为不等待）
    /// </summary>
    public void DisposeAllRunners()
    {
        List<ChatSession> sessions;
        lock (_locker) sessions = _loaded.Values.ToList();
        foreach (ChatSession session in sessions)
        {
            DisposeRunner(session);
        }
    }

    /// <summary>
    /// 释放执行者。删除/卸载是同步流程,释放挂后台尽力而为;
    /// 执行者内部与运行同闸,进行中的轮次结束后才真正释放。
    /// </summary>
    private static void DisposeRunner(ChatSession? session)
    {
        if (session == null) return;
        ValueTask task = session.DisposeRunnerAsync();
        if (task.IsCompleted) return;
        _ = task.AsTask().ContinueWith(
            t => Log.Warning($"Dispose runner failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// 扫描目录重建索引（索引文件丢失或损坏时的恢复路径）
    /// </summary>
    public void RebuildIndex()
    {
        lock (_locker)
        {
            _metas.Clear();
            if (!Directory.Exists(AppPaths.Data.Sessions))
            {
                SaveIndex();
                return;
            }

            foreach (string file in Directory.GetFiles(AppPaths.Data.Sessions, "*" + MetaSuffix))
            {
                ChatSession? session = SaveUtility.Load<ChatSession>(file, SessionJsonOptions.Default);
                if (session == null) continue;

                session.SessionId = Path.GetFileName(file)[..^MetaSuffix.Length];
                _metas[session.SessionId] = session.ToMeta();
            }

            Log.Debug($"Session index rebuilt: {_metas.Count} sessions.");
            SaveIndex();
        }
    }

    //================= 框架附加状态(可丢弃) =================

    /// <summary>
    /// 保存框架附加状态（todos / mode / 审批决定）。
    /// 历史不在其中——历史的权威来源是会话本体，因此该文件丢失只影响侧栏，不丢对话。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="state">框架序列化产物</param>
    public async Task SaveAgentStateAsync(string sessionId, JsonElement state)
    {
        try
        {
            string dir = AppPaths.Data.Sessions;
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(GetAgentStatePath(sessionId), state.GetRawText()).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Error($"Save agent state failed: {e.Message}");
        }
    }

    /// <summary>
    /// 读取框架附加状态
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>状态文档；缺失或损坏为 null</returns>
    public async Task<JsonDocument?> LoadAgentStateAsync(string sessionId)
    {
        string path = GetAgentStatePath(sessionId);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
        }
        catch (Exception e)
        {
            Log.Warning($"Load agent state '{sessionId}' failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 删除框架附加状态。清空历史时必须一并删除，否则 todos/mode/审批会残留，
    /// 下次挂接又被读回来——出现「历史空了但任务清单还在」。
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void DeleteAgentState(string sessionId)
    {
        SaveUtility.Delete(GetAgentStatePath(sessionId));
    }

    //================= 路径 =================

    // 调用方须持有 _locker:枚举 _metas 与写 index.json 都不能与别的线程交错
    private void SaveIndex()
    {
        SaveUtility.Save(GetIndexPath(), _metas.Values.ToList(), SessionJsonOptions.Default);
    }

    private static string GetIndexPath()
    {
        return Path.Combine(AppPaths.Data.Sessions, IndexFileName);
    }

    private static string GetBodyPath(string sessionId)
    {
        return Path.Combine(AppPaths.Data.Sessions, sessionId + ".json");
    }

    private static string GetMetaPath(string sessionId)
    {
        return Path.Combine(AppPaths.Data.Sessions, sessionId + MetaSuffix);
    }

    private static string GetHistoryPath(string sessionId)
    {
        return Path.Combine(AppPaths.Data.Sessions, sessionId + HistorySuffix);
    }

    private static string GetAgentStatePath(string sessionId)
    {
        return Path.Combine(AppPaths.Data.Sessions, sessionId + AgentStateSuffix);
    }
}
