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
/// MCP server 的工具<b>怎么送到模型手里</b>（送达方式）。
///
/// 与托管（连不连）、可用（能不能用）是三个正交的问题，见 ADR 0051。
/// 它是每个 server 一份的本机偏好，不入库、不属于角色。
/// </summary>
public enum EMcpMountMode
{
    /// <summary>
    /// 按需：工具定义不进 <c>tools</c>，模型经固定的元工具 <c>McpHelp</c> / <c>McpCall</c> 先查后调。
    /// 工具集不随连接状态变化，缓存前缀因此稳定。这是默认值。
    /// </summary>
    OnDemand = 0,

    /// <summary>直挂：工具定义直接放进请求的 <c>tools</c>，模型按工具名调用（原有方式）</summary>
    Direct = 1,
}
