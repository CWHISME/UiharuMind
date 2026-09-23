/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json.Serialization;
using UiharuMind.Core.AI.Character;
using UiharuMind.Core.AI.Execution.Assembly;

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
    public string CharacterId { get; set; } = nameof(DefaultCharacter.None);

    /// <summary>记忆库名</summary>
    public string MemoryName { get; set; } = string.Empty;

    /// <summary>绑定的工作目录；为空表示未绑定（仅 agent 会话有意义）</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>权限档索引（EAgentPermissionMode，仅 agent 会话有意义）</summary>
    public int PermissionModeIndex { get; set; } = 1;

    /// <summary>会话覆写的模型名；为空表示无覆写、跟随全局当前模型</summary>
    public string? SessionModelName { get; set; }

    /// <summary>创建时间</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>最后更新时间（列表排序依据）</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>
    /// 最近一次派发/续跑的开始时刻。每次 <c>BackgroundSubAgentDispatcher.Dispatch</c> 写入，
    /// 可空表示旧数据（没有该字段的委派）。仅子会话有意义。
    ///
    /// 用途：右栏「子代理」面板据此显示「本次已运行 / 末轮耗时」，也是运行中置顶排序的键。
    /// 必须落盘——面板可能切走，等切回来时它要能重算而不是靠 UI 计时器续命。
    /// </summary>
    public DateTimeOffset? LastRunStartedAt { get; set; }

    /// <summary>消息条数（列表展示，避免为此加载本体）</summary>
    public int MessageCount { get; set; }

    /// <summary>是否有未发送的输入草稿（列表据此显示小标记，避免为此加载本体）</summary>
    public bool HasComposerDraft { get; set; }

    /// <summary>
    /// 派活给它的那个会话；为空表示这是一个普通会话。非空即<b>子会话</b>：
    /// 不进左栏列表，入口是父会话流里的工具卡片与右栏「子代理」面板，随父会话级联删除。
    /// </summary>
    public string? ParentSessionId { get; set; }

    /// <summary>
    /// 会话是不是子会话（<see cref="ParentSessionId"/> 非空）。
    /// 不入索引文件：它是算出来的，落进 json 就是一个只读的假字段，
    /// 改了 <see cref="ParentSessionId"/> 而它还是旧值时没有任何地方会报错
    /// </summary>
    [JsonIgnore]
    public bool IsSubSession => !string.IsNullOrEmpty(ParentSessionId);

    /// <summary>子会话装配成哪一种子代理（仅子会话有意义）</summary>
    public ESubAgentType SubAgentType { get; set; } = ESubAgentType.General;

    /// <summary>被点名的子智能体名；空串表示通用匿名子代理（仅子会话有意义）</summary>
    public string SubAgentName { get; set; } = string.Empty;

    /// <summary>
    /// 这个子会话是<b>后台派出、报告还没交回</b>。仅子会话有意义。
    ///
    /// 必须落盘：进程被杀时它就是「父会话里那条『已派出』永远等不到下文」的唯一线索，
    /// 启动时据此扫描收口（往父会话落一条「因退出而中止」）。进程内它还是唤醒排队的依据。
    /// 见 [ADR 0025]。
    /// </summary>
    public bool BackgroundReportPending { get; set; }

    /// <summary>是不是群壳会话（ADR 0046）。列表归类要看它，不必为此加载本体</summary>
    public bool IsGroup { get; set; }

    /// <summary>群的类型：智能体群为 true。仅群壳有意义，归哪一侧列表由它决定</summary>
    public bool IsAgentGroup { get; set; }

    /// <summary>所属群壳会话；非空即群成员会话：不进左栏，入口是群的右栏成员列表，随群级联删除</summary>
    public string? GroupId { get; set; }

    /// <summary>会话是不是群成员会话。算出来的，不入索引（理由同 <see cref="IsSubSession"/>）</summary>
    [JsonIgnore]
    public bool IsGroupMember => !string.IsNullOrEmpty(GroupId);
}
