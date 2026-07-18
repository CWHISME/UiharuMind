/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Agent.Mcp;

/// <summary>
/// MCP server 传输类型
/// </summary>
public enum EMcpTransportType
{
    /// <summary>本地子进程(stdio)</summary>
    Stdio,

    /// <summary>远程 HTTP(Streamable HTTP / SSE)</summary>
    Http,
}

/// <summary>
/// 一个 MCP server 的连接配置
/// </summary>
public class McpServerConfig
{
    /// <summary>唯一名称(同时作为工具名前缀)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>传输类型</summary>
    public EMcpTransportType TransportType { get; set; } = EMcpTransportType.Stdio;

    /// <summary>stdio:启动命令(如 npx)</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>stdio:命令参数(空格分隔)</summary>
    public string Arguments { get; set; } = string.Empty;

    /// <summary>http:服务地址</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>环境变量(stdio)</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;
}

/// <summary>
/// MCP server 运行状态
/// </summary>
public enum EMcpConnectionState
{
    Disconnected,
    Connecting,
    Connected,
    Failed,
}
