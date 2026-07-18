/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Agents.AI;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Agent;

/// <summary>
/// 会话元数据(左侧列表展示,不必反序列化整个 session)
/// </summary>
public class AgentSessionMeta
{
    /// <summary>会话唯一标识,同时是会话目录名</summary>
    public string SessionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>会话标题</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>绑定的工作目录;为空表示未绑定</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>权限档索引(EAgentPermissionMode)</summary>
    public int PermissionModeIndex { get; set; } = 1;

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.Now;

    /// <summary>最后更新时间</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
}

/// <summary>
/// 会话索引与持久化:元数据存单一索引文件,会话本体经框架
/// SerializeSessionAsync 序列化为每会话一个 session.json(含历史/todos/mode/审批规则)。
/// </summary>
public class AgentSessionIndex : Singleton<AgentSessionIndex>, IInitialize
{
    private const string IndexFileName = "AgentSessionIndex.json";
    private const string SessionFileName = "session.json";

    private List<AgentSessionMeta> _metas = new();

    public void OnInitialize()
    {
        _metas = SaveUtility.Load<List<AgentSessionMeta>>(GetIndexPath()) ?? new List<AgentSessionMeta>();
    }

    /// <summary>
    /// 获取全部会话元数据,按最后更新时间倒序
    /// </summary>
    /// <returns>元数据列表</returns>
    public List<AgentSessionMeta> GetSessions()
    {
        return _metas.OrderByDescending(x => x.UpdatedAt).ToList();
    }

    /// <summary>
    /// 新建会话元数据并入索引
    /// </summary>
    /// <param name="title">标题</param>
    /// <param name="workspacePath">工作目录</param>
    /// <param name="permissionModeIndex">权限档索引</param>
    /// <returns>元数据</returns>
    public AgentSessionMeta CreateMeta(string title, string? workspacePath, int permissionModeIndex)
    {
        AgentSessionMeta meta = new()
        {
            Title = title,
            WorkspacePath = workspacePath,
            PermissionModeIndex = permissionModeIndex,
        };
        _metas.Add(meta);
        SaveIndex();
        return meta;
    }

    /// <summary>
    /// 保存会话本体与元数据
    /// </summary>
    /// <param name="agent">所属 agent</param>
    /// <param name="session">会话</param>
    /// <param name="meta">元数据</param>
    public async Task SaveSessionAsync(AIAgent agent, AgentSession session, AgentSessionMeta meta)
    {
        try
        {
            JsonElement serialized = await agent.SerializeSessionAsync(session).ConfigureAwait(false);
            string dir = GetSessionDirectory(meta.SessionId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, SessionFileName), serialized.GetRawText())
                .ConfigureAwait(false);

            meta.UpdatedAt = DateTimeOffset.Now;
            SaveIndex();
        }
        catch (Exception e)
        {
            Log.Error($"Save agent session failed: {e.Message}");
        }
    }

    /// <summary>
    /// 加载会话本体;文件缺失或损坏返回 null
    /// </summary>
    /// <param name="agent">目标 agent(需与保存时的 provider 组合兼容)</param>
    /// <param name="sessionId">会话标识</param>
    /// <returns>会话;失败为 null</returns>
    public async Task<AgentSession?> LoadSessionAsync(AIAgent agent, string sessionId)
    {
        string path = Path.Combine(GetSessionDirectory(sessionId), SessionFileName);
        if (!File.Exists(path)) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false));
            return await agent.DeserializeSessionAsync(document.RootElement).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            Log.Warning($"Load agent session '{sessionId}' failed: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 更新元数据(标题、权限档等变更后调用)
    /// </summary>
    /// <param name="meta">元数据</param>
    public void SaveMeta(AgentSessionMeta meta)
    {
        meta.UpdatedAt = DateTimeOffset.Now;
        SaveIndex();
    }

    /// <summary>
    /// 删除会话及其全部数据
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    public void Delete(string sessionId)
    {
        _metas.RemoveAll(x => x.SessionId == sessionId);
        SaveIndex();
        try
        {
            string dir = GetSessionDirectory(sessionId);
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch (Exception e)
        {
            Log.Error($"Delete agent session failed: {e.Message}");
        }
    }

    private void SaveIndex()
    {
        SaveUtility.Save(GetIndexPath(), _metas);
    }

    private static string GetIndexPath()
    {
        return Path.Combine(SettingConfig.SaveAgentDataPath, IndexFileName);
    }

    private static string GetSessionDirectory(string sessionId)
    {
        return Path.Combine(SettingConfig.SaveAgentDataPath, sessionId);
    }
}
