/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 预告名单里的一条：这个会话<b>将会</b>（或本该、却没能）接入的一个 server。
///
/// 与 <see cref="McpServerToolGroup"/> 的分工：那个是<b>实况</b>（装配之后真挂上了什么），
/// 这个是<b>预告</b>（装配之前的预测）。两者不合并，界面上也分区呈现。
/// </summary>
public sealed class McpPlannedServer
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>来源工作区；<c>null</c> 即全局配置</summary>
    public string? WorkspacePath { get; init; }

    /// <summary>是否来自项目级 <c>.mcp.json</c></summary>
    public bool IsWorkspaceScoped => WorkspacePath != null;

    /// <summary>将要执行的命令（stdio）或将要连接的地址（http）</summary>
    public string CommandLine { get; init; } = string.Empty;

    /// <summary>
    /// 本条是被项目级同名配置<b>顶掉</b>的那个全局 server。
    /// 界面要灰显并写明原因——否则用户不知道自己全局那个被吃掉了。
    /// </summary>
    public bool IsShadowed { get; init; }

    /// <summary>送达方式（直挂 / 按需）</summary>
    public EMcpMountMode MountMode { get; init; }

    /// <summary>连接层关着（<c>IsEnabled</c> 为 false，见 ADR 0008）</summary>
    public bool IsHostingOff { get; init; }

    /// <summary>被本角色的黑名单禁用（能力层，见 ADR 0008）</summary>
    public bool IsDisabledByCharacter { get; init; }

    /// <summary>项目级配置尚未通过安全确认</summary>
    public bool NeedsApproval { get; init; }

    /// <summary>此刻的连接状态</summary>
    public required EMcpConnectionState State { get; init; }

    /// <summary>上次连上时的工具数；从未连过为 null</summary>
    public int? LastToolCount { get; init; }

    /// <summary>上次算出的工具定义估算 token；从未连过为 null</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>这一轮真的会挂上去（三道闸门都过了，且已连上）</summary>
    public bool WillBeMounted => !IsShadowed && !IsHostingOff && !IsDisabledByCharacter && !NeedsApproval;
}

/// <summary>
/// 一条待用户确认的项目级 server。确认框上要摆的就是这三样：
/// 谁、来自哪个项目、<b>将要执行什么</b>。
/// </summary>
public sealed class McpApprovalRequest
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>来源工作区</summary>
    public required string WorkspacePath { get; init; }

    /// <summary>将要执行的命令（stdio）或将要连接的地址（http）</summary>
    public required string CommandLine { get; init; }

    /// <summary>
    /// 之前授权过、这次是<b>命令被改了</b>。界面要把它与"新增的一条"分开说——
    /// 前者更值得警惕：那意味着仓库里有人动过将在你机器上执行的东西。
    /// </summary>
    public bool IsChanged { get; init; }
}

/// <summary>
/// 一个 server 的状态快照（UI 展示用）
/// </summary>
public sealed class McpServerStatus
{
    /// <summary>连接状态</summary>
    public EMcpConnectionState State { get; init; }

    /// <summary>此刻已取回、真正可调用的工具数</summary>
    public int ToolCount { get; init; }

    /// <summary>
    /// 上次连上时的工具数；空闲回收或配置变更后仍保留。
    /// 界面据此显示「已断开（上次 12 个工具）」——回收之后报 0 是在撒谎，
    /// 用户会以为这个 server 坏了。从未连过时为 null（那是「不知道」）。
    /// </summary>
    public int? LastToolCount { get; init; }

    /// <summary>工具定义的估算 token 数；尚未取回工具时为 null（那是「不知道」，不是 0）</summary>
    public int? EstimatedTokens { get; init; }

    /// <summary>失败原因；成功或未连接时为 null</summary>
    public string? Error { get; init; }

    /// <summary>server 是否给了自述</summary>
    public bool HasInstructions { get; init; }
}
