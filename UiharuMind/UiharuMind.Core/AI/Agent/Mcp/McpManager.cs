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
///
/// 工具集常驻缓存 + 修订号:装配同步取缓存,绝不等网络——慢启动/挂死的 server
/// 不再卡住会话挂接;后台取回工具后修订号自增,装配快照据此在下次挂接时重建。
/// </summary>
public class McpManager : Singleton<McpManager>, IInitialize
{
    private const string SaveFileName = "McpServers.json";

    private List<McpServerConfig> _servers = new();
    private readonly Dictionary<string, McpClient> _clients = new(); //server 名 -> 已连接客户端
    private readonly Dictionary<string, EMcpConnectionState> _states = new();
    private readonly Dictionary<string, int> _toolCounts = new();
    private readonly SemaphoreSlim _connectLock = new(1, 1);

    private readonly object _cacheLock = new(); //守护配置列表/缓存/修订号/状态表
    private readonly Dictionary<string, IReadOnlyList<AITool>> _serverTools = new(); //server 名 -> 已取回工具
    private readonly HashSet<string> _refreshing = new(); //进行中的后台刷新,防重复触发
    private int _revision; //工具集修订号

    /// <summary>
    /// 工具集修订号:配置增删改与后台取回工具都会使其自增,
    /// 装配快照据此感知 MCP 工具集变化。
    /// </summary>
    public int Revision
    {
        get
        {
            lock (_cacheLock)
            {
                return _revision;
            }
        }
    }

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
        lock (_cacheLock)
        {
            return new List<McpServerConfig>(_servers);
        }
    }

    /// <summary>
    /// 获取 server 的运行状态与工具数
    /// </summary>
    /// <param name="name">server 名</param>
    /// <returns>状态与工具数</returns>
    public (EMcpConnectionState State, int ToolCount) GetServerState(string name)
    {
        lock (_cacheLock)
        {
            return (_states.GetValueOrDefault(name, EMcpConnectionState.Disconnected),
                _toolCounts.GetValueOrDefault(name, 0));
        }
    }

    /// <summary>
    /// 新增或更新 server 配置;缓存失效、断开旧连接,启用中的在后台重连
    /// </summary>
    /// <param name="server">配置</param>
    public void SaveServer(McpServerConfig server)
    {
        lock (_cacheLock)
        {
            int index = _servers.FindIndex(x => x.Name == server.Name);
            if (index >= 0) _servers[index] = server;
            else _servers.Add(server);
            _serverTools.Remove(server.Name);
            _revision++; //配置变化(含启停)即视为工具集变化
        }

        Save();
        DisconnectServer(server.Name);
    }

    /// <summary>
    /// 删除 server 配置
    /// </summary>
    /// <param name="name">server 名</param>
    public void DeleteServer(string name)
    {
        lock (_cacheLock)
        {
            _servers.RemoveAll(x => x.Name == name);
            _serverTools.Remove(name);
            _revision++;
        }

        Save();
        DisconnectServer(name);
    }

    /// <summary>
    /// 同步取已缓存的工具集(启用 server 的并集),绝不等待网络。
    /// 尚未取回工具的启用 server 在后台补连,完成后修订号自增——
    /// 下一次挂接经装配快照差异自动重建,新工具随之生效。
    /// </summary>
    /// <returns>工具列表</returns>
    public IReadOnlyList<AITool> GetCachedTools()
    {
        List<AITool> tools = new();
        lock (_cacheLock)
        {
            foreach (McpServerConfig server in _servers.Where(x => x.IsEnabled))
            {
                if (_serverTools.TryGetValue(server.Name, out IReadOnlyList<AITool>? cached))
                {
                    tools.AddRange(cached);
                }
                else
                {
                    KickRefreshLocked(server);
                }
            }
        }

        return tools;
    }

    /// <summary>
    /// 刷新全部启用 server 的工具缓存并等待完成(设置页测试连接用;单个失败不影响其余)
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        List<McpServerConfig> targets;
        lock (_cacheLock)
        {
            targets = _servers.Where(x => x.IsEnabled).ToList();
        }

        await Task.WhenAll(targets.Select(x => RefreshServerAsync(x, cancellationToken))).ConfigureAwait(false);
    }

    /// <summary>调用方须持有 _cacheLock</summary>
    private void KickRefreshLocked(McpServerConfig server)
    {
        if (!_refreshing.Add(server.Name)) return;
        _ = Task.Run(() => RefreshServerAsync(server, CancellationToken.None));
    }

    private async Task RefreshServerAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        try
        {
            McpClient client = await GetOrConnectAsync(server, cancellationToken).ConfigureAwait(false);
            IReadOnlyList<AIFunction> serverTools = await client
                .ListAgentToolsWithTaskSupportAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
            lock (_cacheLock)
            {
                _serverTools[server.Name] = new List<AITool>(serverTools);
                _toolCounts[server.Name] = serverTools.Count;
                _revision++;
            }
        }
        catch (Exception e)
        {
            Log.Warning($"MCP server '{server.Name}' unavailable: {e.Message}");
            lock (_cacheLock)
            {
                _states[server.Name] = EMcpConnectionState.Failed;
            }
        }
        finally
        {
            lock (_cacheLock)
            {
                _refreshing.Remove(server.Name);
            }
        }
    }

    private async Task<McpClient> GetOrConnectAsync(McpServerConfig server, CancellationToken cancellationToken)
    {
        if (_clients.TryGetValue(server.Name, out McpClient? existing)) return existing;
        await _connectLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_clients.TryGetValue(server.Name, out existing)) return existing;
            SetState(server.Name, EMcpConnectionState.Connecting);
            McpClient client = await McpClient.CreateAsync(CreateTransport(server),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            _clients[server.Name] = client;
            SetState(server.Name, EMcpConnectionState.Connected);
            return client;
        }
        catch
        {
            SetState(server.Name, EMcpConnectionState.Failed);
            throw;
        }
        finally
        {
            _connectLock.Release();
        }
    }

    private void SetState(string name, EMcpConnectionState state)
    {
        lock (_cacheLock)
        {
            _states[name] = state;
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
        lock (_cacheLock)
        {
            _states[name] = EMcpConnectionState.Disconnected;
            _toolCounts.Remove(name);
        }

        _ = client.DisposeAsync();
    }

    private void Save()
    {
        List<McpServerConfig> snapshot;
        lock (_cacheLock)
        {
            snapshot = new List<McpServerConfig>(_servers);
        }

        SaveUtility.SaveRootFile(SaveFileName, snapshot);
    }
}
