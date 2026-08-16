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
/// 由角色自带的 <c>AgentToolConfig.DisabledMcpServers</c> 独家说了算（见 ADR 0008）。
/// 两者不是同一个问题的两个闸，因此不构成 ADR 0003 反对的那种「两层 AND」。
/// </summary>
public class McpServerConfig
{
    /// <summary>唯一名称(即标准配置里 mcpServers 的键)</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// 项目级作用域的工作区根目录；<c>null</c> 即全局作用域。
    ///
    /// <b>作用域的全部信息就在这一条路径上</b>，刻意不另设一个 <c>Scope</c> 枚举——那是这条路径的
    /// 冗余投影，能表达「Global 却带着路径」这类非法组合，而冗余投影迟早与本体对不上。
    ///
    /// 由加载方在读取项目级 <c>.mcp.json</c> 时填上，<b>不进磁盘</b>：
    /// 标准形状里没有这一项，而项目级那份文件是要入库共享的，不该带本机路径。
    /// </summary>
    public string? WorkspacePath { get; set; }

    /// <summary>是否为项目级 server（来自工作区的 <c>.mcp.json</c>）</summary>
    public bool IsWorkspaceScoped => WorkspacePath != null;

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
/// 运行态的索引键：<b>(作用域, 名字)</b>。
///
/// 只按名字索引会让两个 Unity 项目里各有一个叫 <c>unity</c> 的 server <b>互相顶掉连接</b>
/// ——这是项目级作用域这件事里最硬的一处。
///
/// 两半的比较口径<b>刻意不同</b>：名字不区分大小写（用户手写进 json，大小写不该成为一次静默失配，
/// 同 <c>McpManager.DisabledSet</c>）；路径区分大小写——大小写不敏感是文件系统的属性、不是我们的，
/// 猜错会把两个真不同的目录当成同一个。
/// </summary>
internal readonly struct McpServerKey : IEquatable<McpServerKey>
{
    /// <summary>规范化后的工作区绝对路径；全局作用域为空串</summary>
    public string Workspace { get; }

    /// <summary>server 名</summary>
    public string Name { get; }

    public McpServerKey(string? workspacePath, string name)
    {
        Workspace = NormalizeWorkspace(workspacePath);
        Name = name;
    }

    /// <summary>该配置的索引键</summary>
    /// <param name="config">server 配置</param>
    /// <returns>索引键</returns>
    public static McpServerKey Of(McpServerConfig config) => new(config.WorkspacePath, config.Name);

    /// <summary>是否属于全局作用域</summary>
    public bool IsGlobal => Workspace.Length == 0;

    /// <summary>
    /// 工作区路径的规范化：取绝对路径并去掉末尾分隔符。
    /// 同一个目录写成 <c>~/a/b</c> 与 <c>~/a/b/</c> 时必须是同一个键，否则会各连一份子进程。
    /// </summary>
    /// <param name="workspacePath">工作区路径；空表示全局</param>
    /// <returns>规范化路径；全局为空串</returns>
    public static string NormalizeWorkspace(string? workspacePath)
    {
        if (string.IsNullOrEmpty(workspacePath)) return string.Empty;
        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(workspacePath));
        }
        catch (Exception)
        {
            // 路径含非法字符时按原样用:这里的职责是"当键",不是校验路径。
            // 拿不到绝对路径的目录后续读文件也会失败,由那一步报错
            return workspacePath;
        }
    }

    public bool Equals(McpServerKey other)
    {
        return string.Equals(Workspace, other.Workspace, StringComparison.Ordinal)
               && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);
    }

    public override bool Equals(object? obj) => obj is McpServerKey other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Workspace.GetHashCode(StringComparison.Ordinal),
            Name.GetHashCode(StringComparison.OrdinalIgnoreCase));
    }

    public override string ToString() => IsGlobal ? Name : $"{Name}@{Workspace}";
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

    /// <summary>
    /// 项目级配置尚未通过安全确认。<b>绝不启动进程</b>，也不算失败——
    /// 缺的是用户那一下授权，不是连接能力（见 <c>McpTrustStore</c>）。
    /// </summary>
    PendingApproval,
}
