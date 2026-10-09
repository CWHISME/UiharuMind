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
/// 一次装配从 MCP 侧拿的东西：可用的 server 名单，与查、调它们的桥接。
/// MCP 工具不进 <c>tools</c>，模型经扩展的两把元工具先查后调（ADR 0079）
/// </summary>
public sealed class McpToolSet
{
    /// <summary>空集(没有可用的 server)</summary>
    public static readonly McpToolSet Empty = new();

    /// <summary>
    /// 可用的 server 名单（<b>只含配置里就有的信息</b>：名字与说明）。
    /// 系统提示里那行扩展名单与右栏都按它
    /// </summary>
    public IReadOnlyList<McpRosterServer> Servers { get; init; } = [];

    /// <summary>查与调的核心；没有可用 server 为 null</summary>
    internal McpBridge? Bridge { get; init; }
}

/// <summary>
/// 名单里的一个 server：名字与用户填的一句话说明。
/// 刻意<b>不带工具数与连接状态</b>——那是运行时信息，写进系统提示会让它随连接变化
/// </summary>
public sealed class McpRosterServer
{
    /// <summary>server 名</summary>
    public required string Name { get; init; }

    /// <summary>项目级来源的工作区路径；全局 server 为 <c>null</c></summary>
    public string? WorkspacePath { get; init; }

    /// <summary>一句话说明；空即只写名字。项目级 server 恒为空</summary>
    public string Description { get; init; } = string.Empty;
}
