/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Microsoft.Agents.AI.Mcp;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using UiharuMind.Core.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Agent.Mcp;

/// <summary>
/// MCP server 配置管理:持久化配置列表;启用的 server 经官方 SDK 连接,
/// 工具通过 Microsoft.Agents.AI.Mcp 扩展转换为 AIFunction 汇入 agent(统一走审批管道)。
/// </summary>
public class McpManager : Singleton<McpManager>, IInitialize
{
    private const string SaveFileName = "McpServers.json";

    private List<McpServerConfig> _servers = new();
    private readonly Dictionary<string, McpClient> _clients = new(); //server 名 -> 已连接客户端
    private readonly Dictionary<string, EMcpConnectionState> _states = new();
    private readonly Dictionary<string, int> _toolCounts = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    public void OnInitialize()
    {
        _servers = SaveUtility.LoadRootFile<List<McpServerConfig>>(SaveFileName) ?? new List<McpServerConfig>();
    }

    /// <summary>
    /// 获取全部 server 配置
    /// </summary>
    /// <returns>配置列表</returns>
    public List<McpServerConfig> GetServers()
    {
        return new List<McpServerConfig>(_servers);
    }

    /// <summary>
    /// 获取 server 的运行状态与工具数
    /// </summary>
    /// <param name="name">server 名</param>
    /// <returns>状态与工具数</returns>
    public (EMcpConnectionState State, int ToolCount) GetServerState(string name)
    {
        return (_states.GetValueOrDefault(name, EMcpConnectionState.Disconnected),
            _toolCounts.GetValueOrDefault(name, 0));
    }

    /// <summary>
    /// 新增或更新 server 配置;配置变更后断开旧连接待下次按需重连
    /// </summary>
    /// <param name="server">配置</param>
    public void SaveServer(McpServerConfig server)
    {
        int index = _servers.FindIndex(x => x.Name == server.Name);
        if (index >= 0) _servers[index] = server;
        else _servers.Add(server);
        Save();
        DisconnectServer(server.Name);
    }

    /// <summary>
    /// 删除 server 配置
    /// </summary>
    /// <param name="name">server 名</param>
    public void DeleteServer(string name)
    {
        _servers.RemoveAll(x => x.Name == name);
        Save();
        DisconnectServer(name);
    }

    /// <summary>
    /// 汇集全部启用 server 的工具(单个 server 失败不影响其余)
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具列表</returns>
    public async Task<IReadOnlyList<AITool>> GetToolsAsync(CancellationToken cancellationToken = default)
    {
        List<AITool> tools = new();
        foreach (McpServerConfig server in _servers.Where(x => x.IsEnabled))
        {
            try
            {
                McpClient client = await GetOrConnectAsync(server, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<AIFunction> serverTools = await client
                    .ListAgentToolsWithTaskSupportAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                _toolCounts[server.Name] = serverTools.Count;
                tools.AddRange(serverTools);
            }
            catch (Exception e)
            {
                Log.Warning($"MCP server '{server.Name}' unavailable: {e.Message}");
                _states[server.Name] = EMcpConnectionState.Failed;
            }
        }

        return tools;
    }

    private async Task<McpClient> GetOrConnectAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(server.Name, out McpClient? existing)) return existing;
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clients.TryGetValue(server.Name, out existing)) return existing;
            _states[server.Name] = EMcpConnectionState.Connecting;
            McpClient client = await McpClient.CreateAsync(CreateTransport(server),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _clients[server.Name] = client;
            _states[server.Name] = EMcpConnectionState.Connected;
            return client;
        }
        catch
        {
            _states[server.Name] = EMcpConnectionState.Failed;
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private static IClientTransport CreateTransport(McpServerConfig server)
    {
        if (server.TransportType == EMcpTransportType.Http)
        {
            return new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new Uri(server.Url),
                Name = server.Name,
            });
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            Arguments = server.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            EnvironmentVariables = server.EnvironmentVariables.Count > 0
                ? server.EnvironmentVariables.ToDictionary(x => x.Key, string? (x) => x.Value)
                : null,
        });
    }

    private void DisconnectServer(string name)
    {
        if (!_clients.Remove(name, out McpClient? client)) return;
        _states[name] = EMcpConnectionState.Disconnected;
        _toolCounts.Remove(name);
        _ = client.DisposeAsync();
    }

    private void Save()
    {
        SaveUtility.SaveRootFile(SaveFileName, _servers);
    }
}
