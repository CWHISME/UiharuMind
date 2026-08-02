/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.Chat;
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
    /// 按角色种类筛选会话，按最后更新时间倒序。
    /// 角色对话与 agent 对话共用同一个索引，各页面只应列出属于自己那一类的会话。
    /// 种类由角色实时派生而非存进元数据——角色的 Kind 改变时会话随之归类，不会留下过期副本。
    /// </summary>
    /// <param name="kind">角色种类</param>
    /// <returns>元数据列表</returns>
    public List<ChatSessionMeta> GetSessions(ECharacterKind kind)
    {
        return _metas.Values
            .Where(x => KindOf(x) == kind)
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

        ChatSession? session = SaveUtility.Load<ChatSession>(GetBodyPath(sessionId), SessionJsonOptions.Default);
        if (session == null)
        {
            Log.Warning($"Load chat session '{sessionId}' failed.");
            return null;
        }

        session.SessionId = sessionId;
        _loaded[sessionId] = session;
        return session;
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
    /// <summary>
    /// 会话是否仍在索引中(防抖保存据此避免复活已删除的会话)
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>存在返回 true</returns>
    public bool Exists(string sessionId)
    {
        return _metas.ContainsKey(sessionId);
    }

    /// <summary>
    /// 冲刷所有防抖中的保存(应用退出前调用,避免尾部丢失)
    /// </summary>
    public void FlushPendingSaves()
    {
        foreach (ChatSession session in _loaded.Values.Where(x => x.HasPendingSave).ToList())
        {
            session.SaveNow();
        }
    }

    public void Save(ChatSession session)
    {
        if (session.IsTransient) return;

        session.UpdatedAt = DateTimeOffset.Now;
        _loaded[session.SessionId] = session;
        // 本体冗余保存同一份元数据,索引损坏时可据此重建
        SaveUtility.Save(GetBodyPath(session.SessionId), session, SessionJsonOptions.Default);
        _metas[session.SessionId] = session.ToMeta();
        SaveIndex();
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

        _loaded.Remove(sessionId);
        bool wasIndexed = _metas.Remove(sessionId);

        SaveUtility.Delete(GetBodyPath(sessionId));
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

        foreach (string file in Directory.GetFiles(SettingConfig.SaveSessionDataPath, "*.json"))
        {
            string name = Path.GetFileName(file);
            if (name == IndexFileName || name.EndsWith(AgentStateSuffix, StringComparison.Ordinal)) continue;

            ChatSession? session = SaveUtility.Load<ChatSession>(file, SessionJsonOptions.Default);
            if (session == null) continue;

            session.SessionId = Path.GetFileNameWithoutExtension(file);
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

    private static string GetAgentStatePath(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveSessionDataPath, sessionId + AgentStateSuffix);
    }
}
