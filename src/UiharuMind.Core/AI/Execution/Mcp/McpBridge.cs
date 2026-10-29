/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using UiharuMind.Core.AI.Execution.Extensions;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.AI.Execution.Files;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// MCP 的<b>核心</b>：列、查（Help）与调（Call），与"怎么送到模型手里"无关。
/// 现在的载体是扩展的两把元工具（<see cref="ExtensionCalls.HelpName"/> / <see cref="ExtensionCalls.CallName"/>，ADR 0079），
/// 每个 server 在那里是一个扩展，它的工具就是扩展的命令。
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
    /// 等 server 连上的上限：此刻模型已经明确要用这个 server，等一会儿比立刻失败划算。
    /// </summary>
    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    private readonly IMcpServerHost _host;
    private readonly string? _workspacePath;
    private readonly IReadOnlyList<string> _disabledServers;
    private readonly string _spillDirectory;
    private readonly AgentPathResolver? _paths; //落盘目录是会话房间时的路径口径

    /// <param name="host">宿主（查找、连接、读缓存）</param>
    /// <param name="workspacePath">会话的工作区；空表示只有全局 server</param>
    /// <param name="disabledServers">本角色禁用的 server 名单</param>
    /// <param name="spillDirectory">超限结果的落盘目录（会话自己的产出房间）</param>
    /// <param name="paths">落盘目录是会话房间时给出，图片路径写成草稿目录简写；null 写绝对路径</param>
    public McpBridge(IMcpServerHost host, string? workspacePath, IEnumerable<string>? disabledServers,
        string spillDirectory, AgentPathResolver? paths = null)
    {
        _host = host;
        _workspacePath = workspacePath;
        _disabledServers = disabledServers?.ToList() ?? [];
        _spillDirectory = spillDirectory;
        _paths = paths;
    }

    /// <summary>
    /// 这一轮可用的 server 与各自此刻的连接状态（不连接）
    /// </summary>
    /// <returns>名单</returns>
    public IReadOnlyList<McpServerBrief> List() => _host.List(_workspacePath, _disabledServers);

    /// <summary>
    /// <b>全部</b>已配置的 server（含这一轮用不上的）与各自被哪道闸挡着（不连接）
    /// </summary>
    /// <returns>名单；一台都没配为空</returns>
    public IReadOnlyList<McpServerBrief> Overview() => _host.Overview(_workspacePath, _disabledServers);

    /// <summary>
    /// 当场连一台 server 并取回工具清单（无视重连退避）。给模型一个明确的「现在就连」动作：
    /// 查、调本来也会顺带连，但那条路的副作用不在名字里，模型不知道该不该指望它
    /// </summary>
    /// <param name="server">模型给的名字</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>给模型的文本：连上了几条命令，或没连上的原因</returns>
    public async Task<string> OpenAsync(string server, CancellationToken cancellationToken)
    {
        McpServerLookup lookup = _host.Find(_workspacePath, _disabledServers, server);
        if (lookup.Server == null) return Refusal(server, lookup);

        McpServerConfig config = lookup.Server;
        McpServerConnection connection =
            await _host.ConnectAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        string name = ExtensionCalls.ModelName(config.Name);
        if (!connection.IsConnected)
        {
            return $"'{name}' 连接失败，错误：{connection.Error ?? "未知原因"}。\n" +
                   "它会在后台继续重连；过一会儿再连接一次即可，不要改用自己写的脚本或手工 HTTP 请求绕过。";
        }

        return $"已连上 '{name}'：{connection.Tools.Count} 条命令。" +
               $"用 {ExtensionCalls.HelpName} 传 name \"{name}\" 可查看命令清单，再用 {ExtensionCalls.CallName} 调。";
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
        if (string.IsNullOrWhiteSpace(server)) return "参数 `name` 必填。";

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
                return $"MCP server '{config.Name}' 未连接（{connection.Error ?? "未知原因"}），也还没有工具清单缓存。" +
                       "它会自行重连，稍后再试。";
            }

            tools = catalog.Tools;
            instructions = catalog.Instructions;
            note = $"注意：server 当前未连接（{connection.Error ?? "未知原因"}）；" +
                   $"以下是 {catalog.CapturedUtc:yyyy-MM-dd HH:mm} UTC 缓存的工具清单。" +
                   "恢复连接前调用会失败；它会自行重连，稍后重试即可。";
        }

        string text;
        if (string.IsNullOrWhiteSpace(tool))
        {
            // 只在查这个 server 时说(原先每轮写在系统提示里):自带的「列出工具」返回的可能含没开放的
            text = McpHelpFormatter.RenderServer(ExtensionCalls.ModelName(config.Name), instructions, tools, note) +
                   "\n可调用的命令以这里为准：server 自带的「列出工具」之类命令，返回的名单可能包含没开放、调不了的。";
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
                : McpHelpFormatter.RenderTool(ExtensionCalls.ModelName(config.Name), found, note);
        }

        return ToolResultSpill.Limit(text, _spillDirectory, $"{ExtensionCalls.HelpName}_{config.Name}", ToolOutputBudget.Catalog);
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
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool))
            return "参数 `name` 必填，写成 server.tool。";

        McpServerLookup lookup = _host.Find(_workspacePath, _disabledServers, server);
        if (lookup.Server == null) return Refusal(server, lookup);
        McpServerConfig config = lookup.Server;

        McpServerConnection connection =
            await _host.ConnectAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);
        if (!connection.IsConnected)
        {
            return $"MCP server '{config.Name}' 未连接：{connection.Error ?? "未知原因"}。" +
                   "它会自行重连；再调一次也能当场等连接回来，稍后重试这笔调用即可。" +
                   $"用 {ExtensionCalls.HelpName}（不带参数）可看每个扩展的实时状态。";
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
            object? raw = await function.InvokeAsync(BuildArguments(arguments), cancellationToken).ConfigureAwait(false);
            return McpCallResult.Normalize(raw, _spillDirectory, $"{ExtensionCalls.CallName}_{config.Name}_{function.Name}", _paths);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception e) when (!IsConnectionFailure(e))
        {
            return $"MCP 调用 {config.Name}.{function.Name} 失败：{e.Message}";
        }
        catch (Exception e)
        {
            // 传输层故障(server 进程退出、管道断):旧 client 死在运行态上,重试永远失败——
            // 丢掉重连一次,再试这一笔。再失败就不是连接的事了,两段错误一起交代给模型
            McpServerConnection fresh =
                await _host.ReconnectAsync(config, ConnectTimeout, cancellationToken).ConfigureAwait(false);
            if (!fresh.IsConnected)
            {
                return $"MCP 调用 {config.Name}.{function.Name} 失败：{e.Message}；重连也失败（{fresh.Error ?? "未知原因"}）。" +
                       "它会自行重试；再调一次也能当场等连接回来。" +
                       "不要用自己写的脚本或手工 HTTP 请求绕过——那会跳过用户授权。";
            }

            // 重连后必须在新清单里重新认这个工具:旧 function 挂在已释放的 client 上,拿它重试只会再抛
            AIFunction? retryTarget = McpHelpFormatter.FindByName(fresh.Tools, x => x.Name, tool);
            if (retryTarget == null) return UnknownTool(config.Name, tool, fresh.Tools.Select(x => x.Name));
            try
            {
                object? raw = await retryTarget.InvokeAsync(BuildArguments(arguments), cancellationToken)
                    .ConfigureAwait(false);
                return McpCallResult.Normalize(raw, _spillDirectory,
                    $"{ExtensionCalls.CallName}_{config.Name}_{retryTarget.Name}", _paths);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception retryError)
            {
                return $"重连后 MCP 调用 {config.Name}.{retryTarget.Name} 仍失败：{retryError.Message}";
            }
        }
    }

    /// <summary>从模型的 JSON 对象参数拼 AIFunction 的入参；两处调用同源</summary>
    private static AIFunctionArguments BuildArguments(IReadOnlyDictionary<string, JsonElement>? arguments)
    {
        return new AIFunctionArguments(
            arguments?.ToDictionary(x => x.Key, object? (x) => x.Value) ?? new Dictionary<string, object?>());
    }

    /// <summary>
    /// 是不是传输层故障（连接死了，可丢连接重连）：
    /// <see cref="ClientTransportClosedException"/> 是 SDK 对「server 侧面不见了」的正式答法，
    /// 裸的经 IOException（管道断）/HttpRequestException（HTTP 断链）/SocketException（套接字没通，
    /// 它继承 Win32Exception 而非 IOException，不单独认就漏掉）/ObjectDisposedException（client 已释放）冒出。
    ///
    /// <b>两类不算</b>：<see cref="McpException"/> 是 server <b>正常回话</b>（工具报错、没这个方法），
    /// 重连救不了它；文件/路径类的 <see cref="IOException"/>（工具自己要读的文件不在之类）是业务错误，
    /// 认成连接死会白重启一次 server 并把工具重跑一遍。判据看整条异常链——连接故障常被包在里层。
    /// </summary>
    internal static bool IsConnectionFailure(Exception e)
    {
        bool transport = false;
        for (Exception? cur = e; cur != null; cur = cur.InnerException)
        {
            if (cur is McpException) return false;
            if (cur is ClientTransportClosedException or ObjectDisposedException or SocketException) transport = true;
            else if (cur is HttpRequestException http) transport |= IsTransportHttp(http);
            else if (cur is IOException and not FileNotFoundException and not DirectoryNotFoundException
                     and not FileLoadException and not PathTooLongException) transport = true;
        }

        return transport;
    }

    /// <summary>
    /// HTTP 传输的这一笔算不算连接死：<b>拿得到状态码</b>说明 server 回了话，
    /// 4xx 是请求/权限错误，重连救不了它；只有拿不到状态码（DNS/TCP 没通）或超时、5xx
    /// （server 侧暂时不可用）才值得丢连接重连。不这样分，一次业务 400 会被误判成连接死，
    /// 白重启 server 并把工具重跑一遍。
    /// </summary>
    private static bool IsTransportHttp(HttpRequestException e)
    {
        return e.StatusCode is null or HttpStatusCode.RequestTimeout or >= HttpStatusCode.InternalServerError;
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
    /// 并让模型别再重试——重试的每一次都是白付一轮。完整清单在帮助里，这里只给最像的几个。
    /// </summary>
    private static string UnknownTool(string serverName, string tool, IEnumerable<string> available)
    {
        List<string> names = available.ToList();
        string suggestions = McpHelpFormatter.SuggestNames(names, tool);
        string hint = suggestions.Length > 0 ? $" 相似的可调用命令：{suggestions}。" : string.Empty;
        return $"MCP server '{serverName}' 没有可调用的工具 '{tool}'（共暴露 {names.Count} 个）。{hint} " +
               "很可能是工具在 server 端被关掉了；不要重试，告知用户到 server 端开启。 " +
               $"用 {ExtensionCalls.HelpName}，名字 \"{ExtensionCalls.ModelName(serverName)}\" 可列出全部可调用命令。";
    }

    private static string Refusal(string requested, McpServerLookup lookup) => lookup.Status switch
    {
        EMcpLookupStatus.DisabledByCharacter =>
            $"MCP server '{requested}' 被本智能体禁用（角色能力面板里可以打开）。",
        EMcpLookupStatus.NeedsApproval =>
            $"MCP server '{requested}' 来自本工作区的 .mcp.json，尚未经用户授权，不能使用；请用户在应用里确认一次。",
        EMcpLookupStatus.HostingOff =>
            $"MCP server '{requested}' 在应用的设置里被关掉了（设置 → MCP 里打开托管才连得上）。",
        _ => $"没有叫 '{requested}' 的扩展（也没有配置同名的 MCP server）。" +
             $"{ExtensionCalls.HelpName}（不带参数）列出全部扩展，{BuiltinExtensions.Mention(BuiltinExtensions.Mcp, BuiltinExtensions.McpList)} 列出配置过的 server。",
    };
}
