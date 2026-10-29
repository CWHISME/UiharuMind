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
/// 一次装配从 MCP 侧拿的东西：查、调各个 server 的桥接。
///
/// MCP 工具不进 <c>tools</c>，模型经扩展的两把元工具先查后调（ADR 0079）；
/// 名单也不进系统提示（ADR 0096 补记）。桥接每次都现查配置，所以这里**不装名单快照**——
/// 只留一个能从任意时刻的配置里取到东西的入口
/// </summary>
public sealed class McpToolSet
{
    /// <summary>空集(没有宿主:测试与未装配的场合)</summary>
    public static readonly McpToolSet Empty = new();

    /// <summary>查与调的核心；没有宿主为 null</summary>
    internal McpBridge? Bridge { get; init; }
}
