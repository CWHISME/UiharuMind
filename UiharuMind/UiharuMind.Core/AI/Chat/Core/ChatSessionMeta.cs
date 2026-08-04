/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.AI.Character;

namespace UiharuMind.Core.AI.Chat;

/// <summary>
/// 会话元数据。左侧列表只读索引文件里的这些字段，不必反序列化会话本体——
/// agent 会话的工具结果是全量持久化的，本体可以很大，启动时全量加载会卡死。
/// 索引是可重建的缓存，权威数据在本体文件里（本体冗余保存同一份元数据）。
/// </summary>
public class ChatSessionMeta
{
    /// <summary>会话唯一标识，同时是本体文件名</summary>
    public string SessionId { get; set; } = string.Empty;

    /// <summary>标题（纯显示，允许重复，改名不动文件）</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>列表副标题（开场白或角色描述）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>所属角色标识</summary>
    public string CharacterId { get; set; } = nameof(DefaultCharacter.Empty);

    /// <summary>记忆库名</summary>
    public string MemoryName { get; set; } = string.Empty;

    /// <summary>绑定的工作目录；为空表示未绑定（仅 agent 会话有意义）</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>权限档索引（EAgentPermissionMode，仅 agent 会话有意义）</summary>
    public int PermissionModeIndex { get; set; } = 1;

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间（列表排序依据）</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>消息条数（列表展示，避免为此加载本体）</summary>
    public int MessageCount { get; set; }
}
