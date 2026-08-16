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
/// 一个 MCP server 的连接配置。
///
/// <b>本类只管「连接」，不管「谁能用」</b>：<see cref="IsEnabled"/> 决定这个 server 要不要托管
/// ——stdio 意味着拉起一个子进程，是实打实的全局资源开销；而哪个智能体能用它的工具，
/// 由角色自带的 <c>AgentToolConfig.DisabledMcpServers</c> 独家说了算（见 ADR 0007）。
/// 两者不是同一个问题的两个闸，因此不构成 ADR 0003 反对的那种「两层 AND」。
/// </summary>
public class McpServerConfig
{
    /// <summary>唯一名称(即标准配置里 mcpServers 的键)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>传输类型</summary>
    public EMcpTransportType TransportType { get; set; } = EMcpTransportType.Stdio;

    /// <summary>stdio:启动命令(如 npx)</summary>
    public string Command { get; set; } = string.Empty;

    /// <summary>stdio:命令参数。逐项存放——曾经是空格分隔的字符串，含空格的路径会被静默拆坏</summary>
    public List<string> Args { get; set; } = new();

    /// <summary>http:服务地址</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>环境变量(stdio)</summary>
    public Dictionary<string, string> EnvironmentVariables { get; set; } = new();

    /// <summary>请求头(http),用于 Authorization 这类鉴权</summary>
    public Dictionary<string, string> Headers { get; set; } = new();

    /// <summary>是否托管此 server(连接层,非能力层)</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// 是否把 server 自述(MCP initialize 响应里的 instructions)拼进系统提示。
    /// 那段文本长度由 server 决定，本地小模型的窗口吃不消时可单独关掉而仍保留它的工具。
    /// </summary>
    public bool InjectInstructions { get; set; } = true;
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
