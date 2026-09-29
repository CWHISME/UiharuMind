/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.Json;
using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Execution.Tools;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 按需 MCP 的<b>核心</b>：查（Help）与调（Call），与"怎么送到模型手里"无关。
/// 现在的载体是两个固定元工具（<see cref="McpMetaTools"/>）；以后若要加 shell 命令的壳，
/// 只需另写一层薄壳调用同一个核心，本类一行都不用改（见 ADR 0051）。
///
/// 一个实例绑定<b>一次装配的上下文</b>（工作区、角色禁用名单、落盘目录）。
/// 名单是装配时固化的快照：配置一变装配就会重建，不会用到过期的。
///
/// <b>所有失败都是返回值，不是异常</b>：连不上、没这个工具、调用出错，全部变成一条工具结果。
/// 那只追加、不改前缀，模型据此决定重试还是换路。
/// </summary>
internal sealed class McpBridge
{
    /// <summary>
    /// 等 server 连上的上限。比装配期的预连（3 秒）宽松，
    /// 因为此刻模型已经明确要用这个 server，等一会儿比立刻失败划算。
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly IMcpServerHost _host;
    private readonly string? _workspacePath;
    private readonly IReadOnlyList<string> _disabledServers;
    private readonly string _spillDirectory;

    /// <param name="host">宿主（查找、连接、读缓存）</param>
    /// <param name="workspacePath">会话的工作区；空表示只有全局 server</param>
    /// <param name="disabledServers">本角色禁用的 server 名单</param>
    /// <param name="spillDirectory">超限结果的落盘目录（会话自己的产出房间）</param>
    public McpBridge(IMcpServerHost host, string? workspacePath, IEnumerable<string>? disabledServers,
        string spillDirectory)
    {
        _host = host;
        _workspacePath = workspacePath;
        _disabledServers = disabledServers?.ToList() ?? [];
        _spillDirectory = spillDirectory;
    }

    /// <summary>
    /// 查 server 或工具。
    /// </summary>
    /// <param name="server">server 名</param>
    /// <param name="tool">工具名；空则返回 server 概览，否则返回该工具详情</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本</returns>
    public async Task<string> HelpAsync(string? server, string? tool, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server)) return "Parameter `server` is required.";

        McpServerLookup lookup = _host.Find(_workspacePath, _disabledServers, server);
        if (lookup.Server == null) return Refusal(server, lookup);
        McpServerConfig config = lookup.Server;

        McpServerConnection connection =
            await _host.ConnectAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);

        List<McpToolDescriptor> tools;
        string instructions;
        string? note = null;
        if (connection.IsConnected)
        {
            tools = connection.Tools.Select(McpToolDescriptor.From).ToList();
            instructions = connection.Instructions;
        }
        else
        {
            McpToolCatalog? catalog = _host.LoadCatalog(config);
            if (catalog == null)
            {
                return $"MCP server '{config.Name}' is not connected ({connection.Error ?? "unknown error"}) " +
                       "and has no cached tool list yet. Try again later.";
            }

            tools = catalog.Tools;
            instructions = catalog.Instructions;
            note = $"Note: the server is not connected ({connection.Error ?? "unknown error"}); " +
                   $"this is the tool list cached at {catalog.CapturedUtc:yyyy-MM-dd HH:mm} UTC. " +
                   "McpCall will fail until it is back.";
        }

        string text;
        if (string.IsNullOrWhiteSpace(tool))
        {
            text = McpHelpFormatter.RenderServer(config.Name, instructions, tools, note);
        }
        else
        {
            McpToolDescriptor? found = McpHelpFormatter.FindByName(tools, x => x.Name, tool);
            if (found == null && connection.IsConnected)
            {
                // 快照里没有不等于真没有:重取一遍再判断
                connection = await RefreshAsync(config, connection, cancellationToken).ConfigureAwait(false);
                tools = connection.Tools.Select(McpToolDescriptor.From).ToList();
                found = McpHelpFormatter.FindByName(tools, x => x.Name, tool);
            }

            text = found == null
                ? UnknownTool(config.Name, tool, tools.Select(x => x.Name))
                : McpHelpFormatter.RenderTool(config.Name, found, note);
        }

        return ToolResultSpill.Limit(text, _spillDirectory, $"McpHelp_{config.Name}", ToolOutputBudget.Catalog);
    }

    /// <summary>
    /// 调用 server 的一个工具。
    /// </summary>
    /// <param name="server">server 名</param>
    /// <param name="tool">工具名</param>
    /// <param name="arguments">参数对象；无参数传 null</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>字符串，或含非文本块（如图片）的内容列表</returns>
    public async Task<object> CallAsync(string? server, string? tool,
        IReadOnlyDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(server)) return "Parameter `server` is required.";
        if (string.IsNullOrWhiteSpace(tool)) return "Parameter `tool` is required.";

        McpServerLookup lookup = _host.Find(_workspacePath, _disabledServers, server);
        if (lookup.Server == null) return Refusal(server, lookup);
        McpServerConfig config = lookup.Server;

        McpServerConnection connection =
            await _host.ConnectAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        if (!connection.IsConnected)
        {
            return $"MCP server '{config.Name}' is not connected: {connection.Error ?? "unknown error"}. " +
                   "It is retried on every call; McpHelp still shows its last known tools.";
        }

        AIFunction? function = McpHelpFormatter.FindByName(connection.Tools, x => x.Name, tool);
        if (function == null)
        {
            connection = await RefreshAsync(config, connection, cancellationToken).ConfigureAwait(false);
            function = McpHelpFormatter.FindByName(connection.Tools, x => x.Name, tool);
        }

        if (function == null) return UnknownTool(config.Name, tool, connection.Tools.Select(x => x.Name));

        try
        {
            AIFunctionArguments callArguments = new(
                arguments?.ToDictionary(x => x.Key, object? (x) => x.Value) ?? new Dictionary<string, object?>());
            object? raw = await function.InvokeAsync(callArguments, cancellationToken).ConfigureAwait(false);
            return McpCallResult.Normalize(raw, _spillDirectory, $"McpCall_{config.Name}_{function.Name}");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e)
        {
            return $"MCP call {config.Name}.{function.Name} failed: {e.Message}";
        }
    }

    /// 重取失败（连接断了、超时）就保留旧快照：旧的至少还能回答"有什么"
    private async Task<McpServerConnection> RefreshAsync(McpServerConfig config, McpServerConnection current,
        CancellationToken cancellationToken)
    {
        McpServerConnection refreshed =
            await _host.RefreshToolsAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        return refreshed.IsConnected ? refreshed : current;
    }

    /// <summary>
    /// "没有这个工具"的报错。常见原因不是拼错，而是这个工具在 server 端被关掉了
    /// （Unity MCP 就能逐个开关，且它的"列出工具"会照列被关的），所以要明说这一点、
    /// 并让模型别再重试——重试的每一次都是白付一轮。完整清单在 McpHelp 里，这里只给最像的几个。
    /// </summary>
    private static string UnknownTool(string serverName, string tool, IEnumerable<string> available)
    {
        List<string> names = available.ToList();
        string suggestions = McpHelpFormatter.SuggestNames(names, tool);
        string hint = suggestions.Length > 0
            ? $" Similar callable tools: {suggestions}."
            : string.Empty;
        return $"MCP server '{serverName}' has no callable tool '{tool}' ({names.Count} tools are exposed).{hint} " +
               "It may be turned off on the server side; do not retry it, tell the user to enable it there. " +
               "McpHelp with `server` lists every callable tool.";
    }

    private static string Refusal(string requested, McpServerLookup lookup) => lookup.Status switch
    {
        EMcpLookupStatus.MountedDirectly =>
            $"MCP server '{requested}' is mounted directly; call its tools by their own names instead.",
        EMcpLookupStatus.DisabledByCharacter =>
            $"MCP server '{requested}' is disabled for this agent.",
        EMcpLookupStatus.NeedsApproval =>
            $"MCP server '{requested}' comes from this workspace's .mcp.json and has not been approved " +
            "by the user yet, so it cannot be used.",
        EMcpLookupStatus.HostingOff =>
            $"MCP server '{requested}' is turned off in the app's settings.",
        _ => $"Unknown MCP server '{requested}'. Available servers: {McpHelpFormatter.ListNames(lookup.Available)}.",
    };
}
