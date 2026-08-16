/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json.Serialization;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// MCP server 配置的<b>磁盘形态</b>，逐字采用生态事实标准的 <c>mcpServers</c> 形状
/// （Claude Desktop / .mcp.json / VS Code / Cursor 共用）。
///
/// 这样用户可以把别处的配置整段拷进来，也可以把本项目的拷出去。代价是本项目特有的两项
/// （是否托管、是否注入自述）不能写进这里——它们不属于标准，写进去会污染互拷。
/// 那两项另存于 <see cref="McpServerLocalState"/>，按 server 名与本文件对帐。
/// </summary>
internal sealed class McpServersFile
{
    [JsonPropertyName("mcpServers")]
    public Dictionary<string, McpServerEntry> McpServers { get; set; } = new();

    /// <summary>
    /// 从磁盘形态还原为运行期配置
    /// </summary>
    /// <param name="states">本项目特有状态（按 server 名索引）；缺项取默认值</param>
    /// <returns>配置列表</returns>
    public List<McpServerConfig> ToConfigs(IReadOnlyDictionary<string, McpServerLocalState> states)
    {
        List<McpServerConfig> configs = new(McpServers.Count);
        foreach ((string name, McpServerEntry entry) in McpServers)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            McpServerLocalState state = states.GetValueOrDefault(name) ?? new McpServerLocalState();
            configs.Add(entry.ToConfig(name, state));
        }

        return configs;
    }

    /// <summary>
    /// 把运行期配置拆成磁盘上的两份
    /// </summary>
    /// <param name="configs">配置列表</param>
    /// <returns>标准部分与本项目特有部分</returns>
    public static (McpServersFile Standard, Dictionary<string, McpServerLocalState> States) FromConfigs(
        IEnumerable<McpServerConfig> configs)
    {
        McpServersFile file = new();
        Dictionary<string, McpServerLocalState> states = new();
        foreach (McpServerConfig config in configs)
        {
            if (string.IsNullOrWhiteSpace(config.Name)) continue;
            file.McpServers[config.Name] = McpServerEntry.FromConfig(config);
            states[config.Name] = new McpServerLocalState
            {
                IsEnabled = config.IsEnabled,
                InjectInstructions = config.InjectInstructions,
            };
        }

        return (file, states);
    }
}

/// <summary>
/// 标准 <c>mcpServers</c> 里的一项。字段一律可空——各家客户端写出的子集不同，
/// 缺哪一项都要能读进来。
/// </summary>
internal sealed class McpServerEntry
{
    /// <summary>传输类型标记。生态里常见 stdio / http / sse；缺省按有无 url 推断</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")] public string? Command { get; set; }

    [JsonPropertyName("args")] public List<string>? Args { get; set; }

    [JsonPropertyName("env")] public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonPropertyName("headers")] public Dictionary<string, string>? Headers { get; set; }

    public McpServerConfig ToConfig(string name, McpServerLocalState state)
    {
        return new McpServerConfig
        {
            Name = name,
            TransportType = ResolveTransport(),
            Command = Command ?? string.Empty,
            Args = Args ?? new List<string>(),
            Url = Url ?? string.Empty,
            EnvironmentVariables = Env ?? new Dictionary<string, string>(),
            Headers = Headers ?? new Dictionary<string, string>(),
            IsEnabled = state.IsEnabled,
            InjectInstructions = state.InjectInstructions,
        };
    }

    public static McpServerEntry FromConfig(McpServerConfig config)
    {
        bool http = config.TransportType == EMcpTransportType.Http;
        return new McpServerEntry
        {
            Type = http ? "http" : "stdio",
            // 写出时只留该传输用得上的字段:互拷出去的那份不该带着另一种传输的残留
            Command = http ? null : NullIfEmpty(config.Command),
            Args = http || config.Args.Count == 0 ? null : config.Args,
            Env = http || config.EnvironmentVariables.Count == 0 ? null : config.EnvironmentVariables,
            Url = http ? NullIfEmpty(config.Url) : null,
            Headers = !http || config.Headers.Count == 0 ? null : config.Headers,
        };
    }

    /// 显式 type 优先;没写就按"有 url 即远程"推断——生态里省略 type 的写法很常见
    private EMcpTransportType ResolveTransport()
    {
        if (!string.IsNullOrWhiteSpace(Type))
        {
            return Type.Equals("stdio", StringComparison.OrdinalIgnoreCase)
                ? EMcpTransportType.Stdio
                : EMcpTransportType.Http;
        }

        return string.IsNullOrWhiteSpace(Url) ? EMcpTransportType.Stdio : EMcpTransportType.Http;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;
}

/// <summary>
/// 本项目特有、不进标准配置文件的那两项。按 server 名与 <see cref="McpServersFile"/> 对帐，
/// 缺项即取本类的默认值——所以直接手贴一份标准配置进来也能立刻用。
/// </summary>
public sealed class McpServerLocalState
{
    /// <summary>是否托管此 server</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>是否注入 server 自述</summary>
    public bool InjectInstructions { get; set; } = true;
}
