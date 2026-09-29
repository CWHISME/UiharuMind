/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 按配置建立到 MCP server 的客户端连接：传输选择（stdio / Streamable HTTP）与
/// "只认 POST 的引擎"降级。不碰锁、不碰运行态，<see cref="McpManager"/> 只管拿到连接之后的账。
/// </summary>
internal static class McpTransportFactory
{
    /// <summary>
    /// 握手时报给 server 的客户端身份。<b>必须显式给</b>：不给的话 SDK 会拿当前进程的信息顶上，
    /// 于是同一个应用从桌面端连过去叫 <c>UiharuMind.Desktop</c>、从命令行连过去叫 <c>UiharuMind.CLI</c>，
    /// server 那边看到的是两个客户端。这个名字会进 server 日志，是排查问题时的第一个线索。
    /// </summary>
    private static readonly McpClientOptions ClientOptions = new()
    {
        ClientInfo = new Implementation
        {
            Name = AppInfo.Name,
            Version = AppInfo.Version.ToString(),
        },
        // SDK 默认先发一个 server/discover 探测最新协议版本，再退回 initialize。
        // 不认识这个方法的 server（Unity MCP 就是）会直接吞掉它，于是每次新连接白等满 5 秒——
        // 实测首次 McpHelp 6 秒。本地与局域网 server 的响应远快于 2 秒；
        // 万一是很慢的远程 server 误判，后果也只是走 initialize 握手，与今天绝大多数 server 一致
        DiscoverProbeTimeout = TimeSpan.FromSeconds(2),
    };

    /// <summary>
    /// 连上 server。先按官方 SDK transport（Streamable HTTP）连；若引擎只实现纯 POST
    /// （对 GET/SSE 回 405/411），则退级到纯 POST transport 重建。判断见 <see cref="IsPostOnlyRejection"/>。
    /// </summary>
    /// <param name="server">配置</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已握手的客户端</returns>
    public static async Task<McpClient> CreateClientAsync(McpServerConfig server,
        CancellationToken cancellationToken)
    {
        try
        {
            return await McpClient
                .CreateAsync(CreateTransport(server), ClientOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception e) when (IsPostOnlyRejection(e))
        {
            return await McpClient
                .CreateAsync(CreatePostOnlyTransport(server), ClientOptions, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
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
                AdditionalHeaders = server.Headers.Count > 0
                    ? new Dictionary<string, string>(server.Headers)
                    : null,
            });
        }

        return new StdioClientTransport(new StdioClientTransportOptions
        {
            Name = server.Name,
            Command = server.Command,
            // 逐项存放,不再 Split(' '):含空格的路径与带引号的参数曾在这里被静默拆坏
            Arguments = server.Args.Count > 0 ? server.Args.ToArray() : null,
            EnvironmentVariables = server.EnvironmentVariables.Count > 0
                ? server.EnvironmentVariables.ToDictionary(x => x.Key, string? (x) => x.Value)
                : null,
        });
    }

    /// <summary>
    /// 引擎只实现了纯 POST（Streamable HTTP 握手里的 GET/SSE 被回 405、或 POST 没带 Content-Length 回 411）
    /// 时，改用纯 POST transport 连接。
    /// </summary>
    private static IClientTransport CreatePostOnlyTransport(McpServerConfig server)
    {
        return new PostOnlyClientTransport(server.Name, new Uri(server.Url),
            new HttpClient(), server.Headers.Count > 0 ? server.Headers : null);
    }

    /// <summary>
    /// 判断连接失败是否源于「server 只认 POST」：SDK 把 405/411 包在 HttpRequestException 里。
    /// 是则值得回退到纯 POST transport 重试一次，否则不必（是其它真错）。
    /// </summary>
    private static bool IsPostOnlyRejection(Exception e)
    {
        for (Exception? cur = e; cur != null; cur = cur.InnerException)
        {
            if (cur is HttpRequestException)
            {
                string msg = cur.Message;
                if (msg.Contains("405") || msg.Contains("MethodNotAllowed") ||
                    msg.Contains("411") || msg.Contains("LengthRequired"))
                    return true;
            }
        }

        return false;
    }
}
