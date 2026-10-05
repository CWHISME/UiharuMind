using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat.CrossProcess;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Instances;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 同一档案开两个实例时的会话互通（ADR 0064）：盘上的历史是权威，
/// 内存那份记着自己最后见到的历史文件指纹，对不上就是旧的，旧的一律不写盘。
/// 会话头（草稿、标题、用量）只改不算旧——只是打开看看的那一侧切走时也会写草稿，
/// 拿它判旧会把正跑着的那一侧判死
/// </summary>
public partial class SessionManager
{
    private const string IndexLockFileName = "index.lock";
    private static readonly TimeSpan IndexLockTimeout = TimeSpan.FromSeconds(2);

    private readonly ConcurrentDictionary<string, FileStamp> _metaStamps = new(); //本实例最后见到的会话头指纹,只用来认出监视到的是不是自己那一下
    private readonly ConcurrentDictionary<string, FileStamp> _historyStamps = new(); //本实例最后见到的历史文件指纹(读入或自己写完时),判旧看它
    private readonly ConcurrentDictionary<string, byte> _stale = new(); //当集合用:已知被别的实例改过、内存那份已旧的会话
    private readonly ConcurrentDictionary<string, object> _historyGates = new(); //每会话一把:本进程读写历史连同记指纹,与判旧比对互斥
    private readonly ConditionalWeakTable<ChatSession, object> _retired = new(); //退役的本体(被盘上新读的那份顶替,或会话已删),还攥着它的人再写一律拒
    private FileStamp _indexStamp = FileStamp.Missing; //自己最后一次写(或读)索引后的指纹,调用方须持有 _locker
    private SessionFileWatcher? _watcher;
    private readonly Dictionary<string, IDisposable> _pendingWorkLeases = new(); //名下有未了结后台工作的会话持着的租约,锁它自己

    /// <summary>
    /// 别的实例新建、改动或删除了会话，元数据已对到盘上。来自线程池，订阅方自行派发
    /// </summary>
    public event Action? OnSessionsChangedExternally;

    /// <summary>
    /// 开始监视别的实例对会话的改动。应用启动时调一次
    /// </summary>
    public void WatchExternalChanges()
    {
        if (_watcher != null) return;
        _watcher = new SessionFileWatcher(AppPaths.Data.Sessions, MetaSuffix, ApplyExternalChanges);
        PendingWork.Changed += HoldLeaseWhilePendingWork;
    }

    // 派出后台子代理/任务之后,主会话这一轮就结束了,但它还在等报告交回:这期间也归本实例。
    // 只在轮内持租约的话,另一边就能在这段空档里开跑,一写盘这边就判旧,报告写不进、唤醒轮被拒(实机踩到)
    private void HoldLeaseWhilePendingWork(string sessionId)
    {
        bool pending = PendingWork.Has(sessionId);
        lock (_pendingWorkLeases)
        {
            if (pending && !_pendingWorkLeases.ContainsKey(sessionId))
            {
                if (SessionLease.TryAcquire(sessionId) is { } lease) _pendingWorkLeases[sessionId] = lease;
            }
            else if (!pending && _pendingWorkLeases.Remove(sessionId, out IDisposable? held))
            {
                held.Dispose();
            }
        }
    }

    /// <summary>
    /// 只问不占：这个会话此刻能不能在本实例里开跑。界面发送前先问，免得画了气泡再被拒；
    /// 真正开跑仍以 <see cref="TryClaimTurn"/> 为准
    /// </summary>
    /// <param name="session">会话</param>
    /// <returns>不能跑的原因；能跑为 <see cref="ETurnBlock.None"/></returns>
    public ETurnBlock CheckTurnBlock(ChatSession session) =>
        IsUnsaved(session) ? ETurnBlock.None : BlockOf(session.SessionId, IsRetired(session));

    /// <summary>
    /// 一轮开跑前认领会话：内存那份不能是旧的，也不能正在别的实例里跑。持到这一轮彻底结束
    /// </summary>
    /// <param name="session">会话</param>
    /// <param name="block">认领不到的原因；认领到了为 <see cref="ETurnBlock.None"/></param>
    /// <returns>归还用的句柄；认领不到为 null</returns>
    public IDisposable? TryClaimTurn(ChatSession session, out ETurnBlock block)
    {
        block = ETurnBlock.None;
        if (IsUnsaved(session)) return EmptyScope.Instance;

        if (IsOutdated(session))
        {
            block = ETurnBlock.StaleCopy;
            return null;
        }

        IDisposable? lease = SessionLease.TryAcquire(session.SessionId);
        if (lease == null) block = ETurnBlock.RunningElsewhere;
        return lease;
    }

    /// <summary>
    /// 不能跑的原因写成日志用的一句英文。给用户看的措辞由界面按 <see cref="ETurnBlock"/> 本地化
    /// </summary>
    /// <param name="block">原因</param>
    /// <returns>日志文案</returns>
    public static string Describe(ETurnBlock block) => block switch
    {
        ETurnBlock.StaleCopy => "this copy is outdated: another instance changed the conversation",
        ETurnBlock.RunningElsewhere => "the conversation is running in another instance",
        _ => "nothing blocks it",
    };

    private static bool IsUnsaved(ChatSession session) => session.IsTransient || string.IsNullOrEmpty(session.SessionId);

    private bool IsRetired(ChatSession session) => _retired.TryGetValue(session, out _);

    // 问、认领、写盘拦截三处共用这一个判据。先看租约:对方正跑着时它一直在写盘,这边多半也已判旧,
    // 该告诉用户的是「等那边结束」而不是「切走再切回来」
    private ETurnBlock BlockOf(string sessionId, bool retired)
    {
        if (SessionLease.IsHeldElsewhere(sessionId)) return ETurnBlock.RunningElsewhere;
        return retired || IsStaleOnDisk(sessionId) ? ETurnBlock.StaleCopy : ETurnBlock.None;
    }

    /// <summary>
    /// 取这个会话连同名下子会话、群成员的租约，全部到手才算数。删除用：
    /// 只查不占的话，查完到逐个删之间对方开跑，删到一半撞上，父会话没了它们就成了孤儿
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="leases">取到的租约，调用方负责归还（取不全时也要还已取到的）</param>
    /// <returns>全部取到为 true；有一个正在别的实例里跑为 false</returns>
    private bool TryClaimTree(string sessionId, List<IDisposable> leases)
    {
        if (SessionLease.TryAcquire(sessionId) is not { } lease) return false;
        leases.Add(lease);
        return GetSubSessions(sessionId).Concat(GetGroupMembers(sessionId)).All(x => TryClaimTree(x.SessionId, leases));
    }

    // 会话删掉了,还攥着本体的人(收尾中的子代理、打开着的视图)再写会把它写回来:一并退役
    private void RetireDeleted(ChatSession? session)
    {
        if (session != null) _retired.AddOrUpdate(session, session);
    }

    /// <summary>
    /// 这个本体是不是旧的：已被盘上新读的那份顶替，或别的实例在本实例最后一次读写之后改过它的历史
    /// </summary>
    /// <param name="session">会话本体</param>
    /// <returns>是否已旧</returns>
    public bool IsOutdated(ChatSession session) => IsRetired(session) || IsStaleOnDisk(session.SessionId);

    /// <summary>
    /// 内存那份是不是旧的：别的实例在本实例最后一次读写之后改过它的历史。
    /// 查到变了会顺手记进旧会话集合，此后不再取指纹
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>是否已旧</returns>
    public bool IsStaleOnDisk(string sessionId)
    {
        if (_stale.ContainsKey(sessionId)) return true;
        lock (HistoryGate(sessionId))
        {
            // 没记过指纹:本实例没读写过它(新建还没落盘的也在此列),谈不上旧
            if (!_historyStamps.TryGetValue(sessionId, out FileStamp known)) return false;
            if (FileStamp.Of(GetHistoryPath(sessionId)) == known) return false;

            _stale[sessionId] = 0;
            return true;
        }
    }

    /// <summary>
    /// 写盘前的拦截：旧本体、或这个会话正在别的实例里跑，都不写。
    /// 拿旧内容覆盖别人的新内容，比丢掉这一侧的改动更糟
    /// </summary>
    private bool RefuseWrite(ChatSession session, string operation) =>
        RefuseWrite(session.SessionId, operation, IsRetired(session));

    private bool RefuseWrite(string sessionId, string operation, bool replaced = false)
    {
        ETurnBlock block = BlockOf(sessionId, replaced);
        if (block == ETurnBlock.None) return false;

        Log.Warning($"Skipped {operation} of session '{sessionId}': {Describe(block)}.");
        return true;
    }

    private void RememberMetaStamp(string sessionId) => _metaStamps[sessionId] = FileStamp.Of(GetMetaPath(sessionId));

    private object HistoryGate(string sessionId) => _historyGates.GetOrAdd(sessionId, _ => new object());

    // 写历史与记指纹对判旧是一步:两步之间被别的线程判旧(存会话头、Load 都会判),
    // 自己刚写的就被当成别的实例写的,会话从此写不进盘(实机踩到)
    private void WriteHistoryTracked(string sessionId, Action<string> write)
    {
        lock (HistoryGate(sessionId))
        {
            string path = GetHistoryPath(sessionId);
            write(path);
            _historyStamps[sessionId] = FileStamp.Of(path);
        }
    }

    // 指纹先于读取取:读完再取的话,中间别人写的那一下会被当成自己见过的。
    // 只替缓存里那个本体记账:被顶替的旧本体重载历史时记下的指纹,会让新读的那份被误判
    private List<ChatMessage> LoadHistoryTracked(ChatSession session)
    {
        string sessionId = session.SessionId;
        // 不拿 _locker:重载回调可能撞进任何持锁代码。闸只管本会话的历史文件,是叶子锁
        lock (HistoryGate(sessionId))
        {
            FileStamp stamp = FileStamp.Of(GetHistoryPath(sessionId));
            List<ChatMessage> history = LoadHistory(sessionId);
            if (!IsRetired(session)) _historyStamps[sessionId] = stamp;
            return history;
        }
    }

    // 卸载前的回写:与盘上一字不差就不写。原样重写也会换掉指纹,
    // 另一个正跑着这个会话的实例就会把自己判成旧的,从此写不进盘
    private void WriteBackHistory(string sessionId, IReadOnlyList<ChatMessage> history)
    {
        string path = GetHistoryPath(sessionId);
        string text = HistoryJsonl.SerializeLines(history);
        try
        {
            if (File.Exists(path) && File.ReadAllText(path) == text) return;
        }
        catch (Exception)
        {
            // 读不出来就照写
        }

        WriteHistoryTracked(sessionId, x => SaveUtility.SaveText(x, text));
    }

    // 摘掉旧本体,下次取用重新读盘。调用方须持有 _locker
    private ChatSession? DropStaleLoaded(string sessionId)
    {
        _loaded.TryGetValue(sessionId, out ChatSession? session);
        // 先记成被顶替:判「有没有人持有」在锁外,摘的这一刻恰好被钉住的那个持有者也写不回去
        if (session != null) _retired.AddOrUpdate(session, session);
        ForgetLoaded(sessionId);
        return session;
    }

    // 本体与它的全部跨进程记账一起忘掉。调用方须持有 _locker
    private void ForgetLoaded(string sessionId)
    {
        _loaded.Remove(sessionId);
        _resident.TryRemove(sessionId, out _);
        _metaStamps.TryRemove(sessionId, out _);
        _historyStamps.TryRemove(sessionId, out _);
        _stale.TryRemove(sessionId, out _);
    }

    // 读会话头并补齐装载时的默认值;读不出来为 null
    private static ChatSession? LoadHeader(string path, string sessionId)
    {
        ChatSession? header = SaveUtility.Load<ChatSession>(path, SessionJsonOptions.Default);
        if (header == null) return null;
        header.SessionId = sessionId;
        // 老数据定格（ADR 0050）：没存形态的会话按当前身份补写。此后身份翻转不再挪已有会话
        if (header.IsAgentForm == null) header.IsAgentForm = header.CharacterData.IsAgent;
        return header;
    }

    // 被持有(界面钉着)或在跑的本体不能换,只能标旧等它放手。会进别处的锁,别在 _locker 内调
    private bool IsHeld(string sessionId) =>
        _pins.ContainsKey(sessionId) || Running.IsBusy(sessionId) || PendingWork.Has(sessionId);

    /// <summary>
    /// 把一批别人改过的会话对到盘上。只动内存，不为别人的改动写索引
    /// </summary>
    /// <param name="sessionIds">会话标识</param>
    internal void ApplyExternalChanges(IReadOnlyCollection<string> sessionIds)
    {
        bool changed = false;
        List<ChatSession> released = new();
        foreach (string sessionId in sessionIds)
        {
            string path = GetMetaPath(sessionId);
            FileStamp stamp = FileStamp.Of(path);
            // 指纹与自己记的一致:就是自己刚写的
            if (_metaStamps.TryGetValue(sessionId, out FileStamp known) && known == stamp) continue;

            ChatSessionMeta? meta = null;
            if (stamp != FileStamp.Missing)
            {
                ChatSession? header = LoadHeader(path, sessionId);
                if (header == null) continue; //写到一半读不出来,等下一批
                meta = header.ToMeta();
            }

            // 只改了会话头(草稿、标题)不动本体:那一侧切走时就会写草稿,判旧会把这边正跑着的判死
            bool historyChanged = IsStaleOnDisk(sessionId);
            bool held = historyChanged && IsHeld(sessionId); //锁外问,理由见 ResolveLoaded
            lock (_locker)
            {
                if (historyChanged && _loaded.ContainsKey(sessionId))
                {
                    if (held) _stale[sessionId] = 0;
                    else if (DropStaleLoaded(sessionId) is { } dropped) released.Add(dropped);
                }
                else if (_loaded.ContainsKey(sessionId))
                {
                    _metaStamps[sessionId] = stamp; //认下这次会话头,别再当成新改动处理
                }

                if (meta != null) _metas[sessionId] = meta;
                else _metas.Remove(sessionId);
            }

            changed = true;
        }

        foreach (ChatSession session in released) DisposeRunner(session);
        if (changed) OnSessionsChangedExternally?.Invoke();
    }

    /// <summary>
    /// 写索引。盘上那份不是自己上次写的，就先把别人的增删并进来再写。
    /// 调用方须持有 <c>_locker</c>
    /// </summary>
    private void SaveIndex()
    {
        string path = GetIndexPath();
        using IDisposable? fileLock =
            ExclusiveFileLock.Acquire(Path.Combine(AppPaths.Data.Sessions, IndexLockFileName), IndexLockTimeout);
        if (fileLock == null) Log.Warning("Session index lock timed out; writing without it.");

        if (FileStamp.Of(path) != _indexStamp) MergeIndexFromDisk(path);
        SaveUtility.Save(path, _metas.Values.ToList(), SessionJsonOptions.Default);
        _indexStamp = FileStamp.Of(path);
    }

    // 盘上有、自己没有:会话头还在就是别人新建的;自己有、盘上没有:会话头没了就是别人删的
    private void MergeIndexFromDisk(string path)
    {
        List<ChatSessionMeta>? disk = SaveUtility.Load<List<ChatSessionMeta>>(path, SessionJsonOptions.Default);
        if (disk == null) return;

        HashSet<string> onDisk = new();
        foreach (ChatSessionMeta meta in disk)
        {
            if (string.IsNullOrEmpty(meta.SessionId)) continue;
            onDisk.Add(meta.SessionId);
            if (_metas.TryGetValue(meta.SessionId, out ChatSessionMeta? mine))
            {
                if (meta.UpdatedAt > mine.UpdatedAt)
                {
                    FreezeForm(meta);
                    _metas[meta.SessionId] = meta;
                }
            }
            else if (File.Exists(GetMetaPath(meta.SessionId)))
            {
                FreezeForm(meta);
                _metas[meta.SessionId] = meta;
            }
        }

        foreach (string sessionId in _metas.Keys.ToList())
        {
            if (!onDisk.Contains(sessionId) && !File.Exists(GetMetaPath(sessionId))) _metas.Remove(sessionId);
        }
    }

    private sealed class EmptyScope : IDisposable
    {
        public static readonly EmptyScope Instance = new();

        public void Dispose()
        {
        }
    }
}
