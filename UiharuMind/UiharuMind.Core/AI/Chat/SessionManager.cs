/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
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

    private readonly Dictionary<string, ChatSessionMeta> _metas = new();

    // 已加载的本体(含临时会话)。临时会话只存在于此,不落盘也不进 _metas。
    private readonly Dictionary<string, ChatSession> _loaded = new();

    public void OnInitialize()
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

    /// <summary>
    /// 全部会话元数据，按最后更新时间倒序
    /// </summary>
    /// <returns>元数据列表</returns>
    public List<ChatSessionMeta> GetSessions()
    {
        return _metas.Values.OrderByDescending(x => x.UpdatedAt).ToList();
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
        return _metas.Values
            .Where(predicate)
            .OrderByDescending(x => x.UpdatedAt)
            .ToList();
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
    /// 取元数据
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>元数据；不存在为 null</returns>
    public ChatSessionMeta? GetMeta(string sessionId)
    {
        return _metas.GetValueOrDefault(sessionId);
    }

    /// <summary>
    /// 按需加载会话本体（含内存中的临时会话），结果会被缓存
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话；文件缺失或损坏为 null</returns>
    public ChatSession? Load(string sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return null;
        if (_loaded.TryGetValue(sessionId, out ChatSession? cached)) return cached;

        ChatSession? session = SaveUtility.Load<ChatSession>(GetMetaPath(sessionId), SessionJsonOptions.Default);
        if (session == null)
        {
            Log.Warning($"Load chat session '{sessionId}' failed.");
            return null;
        }

        session.SessionId = sessionId;
        session.History = LoadHistory(sessionId);
        _loaded[sessionId] = session;
        return session;
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

        _loaded[session.SessionId] = session;
        return session;
    }

    /// <summary>
    /// 把会话纳入索引并落盘
    /// </summary>
    /// <param name="session">会话</param>
    public void Add(ChatSession session)
    {
        session.IsTransient = false;
        _loaded[session.SessionId] = session;
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
    public void SaveMeta(ChatSession session)
    {
        if (session.IsTransient) return;

        session.UpdatedAt = DateTimeOffset.Now;
        _loaded[session.SessionId] = session;
        // 会话头冗余保存同一份元数据,索引损坏时可据此重建
        SaveUtility.Save(GetMetaPath(session.SessionId), session, SessionJsonOptions.Default);
        _metas[session.SessionId] = session.ToMeta();
        SaveIndex();
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
        // 附件路径记在本体里,所以要在删文件之前把它读出来
        ChatSession? session = Load(sessionId);
        DeleteOwnedAttachments(session);
        DisposeRunner(session);

        _loaded.Remove(sessionId);
        bool wasIndexed = _metas.Remove(sessionId);

        SaveUtility.Delete(GetMetaPath(sessionId));
        SaveUtility.Delete(GetHistoryPath(sessionId));
        SaveUtility.Delete(GetBodyPath(sessionId)); //旧单文件格式残留
        SaveUtility.Delete(GetAgentStatePath(sessionId));
        if (wasIndexed) SaveIndex();

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
        if (!_loaded.Remove(sessionId, out ChatSession? session)) return;
        DisposeRunner(session);
    }

    /// <summary>
    /// 释放全部已加载会话的执行者（应用退出时调用，尽力而为不等待）
    /// </summary>
    public void DisposeAllRunners()
    {
        foreach (ChatSession session in _loaded.Values)
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
        _metas.Clear();
        if (!Directory.Exists(SettingConfig.SaveSessionDataPath))
        {
            SaveIndex();
            return;
        }

        foreach (string file in Directory.GetFiles(SettingConfig.SaveSessionDataPath, "*" + MetaSuffix))
        {
            ChatSession? session = SaveUtility.Load<ChatSession>(file, SessionJsonOptions.Default);
            if (session == null) continue;

            session.SessionId = Path.GetFileName(file)[..^MetaSuffix.Length];
            _metas[session.SessionId] = session.ToMeta();
        }

        Log.Debug($"Session index rebuilt: {_metas.Count} sessions.");
        SaveIndex();
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
            string dir = SettingConfig.SaveSessionDataPath;
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

    private void SaveIndex()
    {
        SaveUtility.Save(GetIndexPath(), _metas.Values.ToList(), SessionJsonOptions.Default);
    }

    private static string GetIndexPath()
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, IndexFileName);
    }

    private static string GetBodyPath(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, sessionId + ".json");
    }

    private static string GetMetaPath(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, sessionId + MetaSuffix);
    }

    private static string GetHistoryPath(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, sessionId + HistorySuffix);
    }

    private static string GetAgentStatePath(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, sessionId + AgentStateSuffix);
    }
}
