/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;
using UiharuMind.Core.AI.Execution.Mcp;
using Xunit;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// PostOnly HTTP 传输与「纯 POST + JSON-RPC 错误体」server（UE/Unity MCP 一类）的兼容回归。
///
/// 背景：SDK 官方主通道连这类 server 时，GET（SSE 流）被 405、不带 Content-Length 的 POST 被 411 拒绝，
/// 触发 <see cref="McpTransportFactory"/> 降级到 <see cref="PostOnlyHttpTransport"/>。PostOnly 发
/// server/discover 探测时，server 回 400 + JSON-RPC MethodNotFound——曾把该响应当连接错误直接抛异常，
/// 导致 SDK 的 discover 探测挂起（真机连 UE 实测 45 秒+ 无返回）。修复：解析 JSON-RPC 错误体并送达
/// 消息通道，让 SDK 退回首版 initialize 握手。
/// </summary>
public class McpPostOnlyHttpCompatTests
{
    /// <summary>
    /// 端到端：工厂连一个「UE 脾气」的 HTTP server——GET 405、无 Content-Length 的 POST 411、
    /// server/discover 400+MethodNotFound、initialize/tools/list 正常——应自动降级 PostOnly、
    /// 探测被拒后回退 initialize 并成功拿到工具。
    /// </summary>
    [Fact]
    public async Task CreateClient_AgainstPostOnlyServer_FallsBackAndConnects()
    {
        await using var stub = new UeLikeMcpStub();
        var config = new McpServerConfig
        {
            Name = "ue-like",
            TransportType = EMcpTransportType.Http,
            Url = $"http://127.0.0.1:{stub.Port}/mcp",
        };

        // 三处都带限时，测试绝不能被挂住拖死（旧行为实测会卡）：连接 30s、取工具 10s、断开 15s
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Task<McpClient> connect = McpTransportFactory.CreateClientAsync(config, CancellationToken.None);
        McpClient client = await connect.WaitAsync(TimeSpan.FromSeconds(30));
        sw.Stop();
        // 防退化成「discover 探测超时兜底」：真回退应在 1s 内完成（UE 真机实测 0.3s），
        // 等满 DiscoverProbeTimeout 10s 说明回退没走正路（评审 P2-2）
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3), $"连接耗时 {sw.Elapsed}，应远小于 10s 探测超时");

        try
        {
            var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(2, tools.Count);

            // 请求序列应完整走过：主通道 405/411 → 降级 → discover 被拒 → initialize → tools/list
            string[] methods = stub.Methods.ToArray();
            Assert.Contains("server/discover", methods);
            Assert.Contains("initialize", methods);
            Assert.Contains("tools/list", methods);
            // server/discover 必须发生在 initialize 之前（回退发生在探测被拒之后）
            Assert.True(Array.IndexOf(methods, "server/discover") < Array.IndexOf(methods, "initialize"),
                "期望先探测 server/discover、被 400 拒后再回退 initialize");
        }
        finally
        {
            // PostOnly 曾因通道永不结束而让 McpClient.DisposeAsync 挂死，这里也限时保护
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        }
    }

    /// <summary>
    /// 非 JSON 错误体（500 + HTML）必须原样带状态码抛 HttpRequestException，
    /// 不能被 JSON 解析路径吞成 JsonException 丢掉状态码（评审 P1-2 场景）。
    /// </summary>
    [Fact]
    public async Task NonJsonError_KeepsHttpStatusCode()
    {
        await using var stub = new UeLikeMcpStub(failNonJson: true);
        var config = new McpServerConfig
        {
            Name = "ue-like",
            TransportType = EMcpTransportType.Http,
            Url = $"http://127.0.0.1:{stub.Port}/mcp",
        };

        Exception? thrown = null;
        try
        {
            // 主通道 405/411 → 降级 PostOnly → discover 收到 500+HTML
            Task<McpClient> connect = McpTransportFactory.CreateClientAsync(config, CancellationToken.None);
            McpClient client = await connect.WaitAsync(TimeSpan.FromSeconds(30));
            await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            throw; // 测试自身限时到期，原样失败，别吞
        }
        catch (Exception ex)
        {
            thrown = ex;
        }

        // 异常链里应有 PostOnly 带出的 500（而不是把 500+HTML 解析成 JsonException）
        Assert.NotNull(thrown);
        Assert.NotNull(FindExceptionByStatus(thrown!, System.Net.HttpStatusCode.InternalServerError));
    }

    private static HttpRequestException? FindExceptionByStatus(Exception ex, System.Net.HttpStatusCode code)
    {
        for (Exception? e = ex; e != null; e = e.InnerException)
        {
            if (e is HttpRequestException hre && hre.StatusCode == code)
                return hre;
        }

        return null;
    }

    /// <summary>
    /// UE 编辑器重启后旧 session 失效（内存态随进程丢失）：带旧 session 的任意 RPC 回 404
    /// Unknown session id。客户端应识别为「可丢连接重连」的连接故障（McpBridge 的判据），
    /// 而不是当成普通业务 404 直接放弃。
    /// </summary>
    [Fact]
    public async Task SessionExpired_AfterServerRestart_IsConnectionFailure_AndCanReconnect()
    {
        await using var stub = new UeLikeMcpStub();
        var config = new McpServerConfig
        {
            Name = "ue-like",
            TransportType = EMcpTransportType.Http,
            Url = $"http://127.0.0.1:{stub.Port}/mcp",
        };

        // 第一段：正常连接 + 取工具
        McpClient client = await McpTransportFactory.CreateClientAsync(config, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));
        var tools = await client.ListToolsAsync(cancellationToken: CancellationToken.None)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, tools.Count);

        // 第二段：模拟 UE 重启，旧 session 失效
        stub.ExpireSession();
        Exception? expiry = null;
        try
        {
            _ = await client.ListToolsAsync(cancellationToken: CancellationToken.None)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            throw;
        }
        catch (Exception ex)
        {
            expiry = ex;
        }

        // 关键：上层（McpBridge.CallAsync）要把这当成连接故障丢连接重连，而不是业务 404 放弃
        Assert.NotNull(expiry);
        Assert.True(McpBridge.IsConnectionFailure(expiry!), $"应判定为可重连的连接故障，实际异常：{expiry!.GetType().Name}: {expiry.Message}");
        Assert.Contains("SESSION-EXPIRED", stub.Methods.ToArray());

        // 第三段：丢旧连接重连应立刻成功（discover 400 → 回退 initialize → 新 session）
        await using McpClient reconnected = await McpTransportFactory.CreateClientAsync(config, CancellationToken.None)
            .WaitAsync(TimeSpan.FromSeconds(30));
        var tools2 = await reconnected.ListToolsAsync(cancellationToken: CancellationToken.None)
            .AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(2, tools2.Count);

        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));
    }

    /// <summary>
    /// 纯 POST 桩：GET 一律 405；POST 不带 Content-Length 一律 411（missing_content_length_header）；
    /// 带 Content-Length 的按 JSON-RPC 方法应答——server/discover 回 400 MethodNotFound，
    /// initialize/tools/list 正常回 2xx。行为对齐 UE 内置 MCP server（实测 2026-11-16）。
    /// </summary>
    private sealed class UeLikeMcpStub : IAsyncDisposable
    {
        private readonly TcpListener _listener;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _acceptLoop;
        private readonly ConcurrentQueue<string> _methods = new();
        private readonly bool _failNonJson; // 为真时所有带体 POST 回 500 + HTML（非 JSON 错误）
        private readonly ConcurrentDictionary<string, byte> _validSessions = new(); // 当前有效的 session id 集合
        private int _issue; // initialize 发放新 session 的流水号

        public UeLikeMcpStub(bool failNonJson = false)
        {
            _failNonJson = failNonJson;
            _validSessions["stub-session-1"] = 1;
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _acceptLoop = Task.Run(AcceptLoopAsync);
        }

        /// <summary>模拟 UE 编辑器重启：全部旧 session 作废，但新 initialize 拿到的 session 仍有效</summary>
        public void ExpireSession() => _validSessions.Clear();

        public int Port { get; }

        /// <summary>按到达顺序记录每个请求的 JSON-RPC method（非 JSON-RPC 请求记方法原文）</summary>
        public ConcurrentQueue<string> Methods => _methods;

        public async ValueTask DisposeAsync()
        {
            _cts.Cancel();
            _listener.Stop();
            try { await _acceptLoop; } catch (OperationCanceledException) { }
            _cts.Dispose();
        }

        private async Task AcceptLoopAsync()
        {
            while (!_cts.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await _listener.AcceptTcpClientAsync(_cts.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }

                _ = HandleConnectionAsync(client);
            }
        }

        private async Task HandleConnectionAsync(TcpClient client)
        {
            using (client)
            using (Stream stream = client.GetStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true))
            {
                while (true)
                {
                    string? requestLine = await reader.ReadLineAsync();
                    if (requestLine == null) return;

                    string method = requestLine.Split(' ')[0];

                    // 解析请求头；特别关注有没有 Content-Length（没有 = UE 会 411 拒）和 Mcp-Session-Id
                    long contentLength = -1;
                    string? sessionIdHeader = null;
                    while (true)
                    {
                        string? headerLine = await reader.ReadLineAsync();
                        if (headerLine == null) return;
                        if (headerLine.Length == 0) break;

                        if (headerLine.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        {
                            string value = headerLine["Content-Length:".Length..].Trim();
                            contentLength = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
                        }
                        else if (headerLine.StartsWith("Mcp-Session-Id:", StringComparison.OrdinalIgnoreCase))
                        {
                            sessionIdHeader = headerLine["Mcp-Session-Id:".Length..].Trim();
                        }
                    }

                    if (method != "POST")
                    {
                        // GET（SSE 流探测）：UE 回 405，且不做流
                        _methods.Enqueue(method);
                        Send(stream, "405 Method Not Allowed", "text/plain;charset=utf-8", string.Empty);
                        return; // 响应后关连接：405 后客户端会重新建立连接，保持桩行为简单
                    }

                    string body = string.Empty;
                    if (contentLength > 0)
                    {
                        var buffer = new char[contentLength];
                        int read = 0;
                        while (read < contentLength)
                        {
                            int chunk = await reader.ReadAsync(buffer, read, (int)(contentLength - read));
                            if (chunk == 0) break;
                            read += chunk;
                        }

                        body = new string(buffer, 0, read);
                    }

                    (int status, string reason, string payload, string? sessionId) =
                        HandlePost(method, contentLength, body, sessionIdHeader);
                    Send(stream, $"{status} {reason}", "application/json;charset=utf-8", payload, sessionId);
                    if (contentLength < 0) return; // 无 Content-Length 的请求体没读，余留脏数据，响应后关连接
                }
            }
        }

        private (int Status, string Reason, string Payload, string? SessionId) HandlePost(
            string method, long contentLength, string body, string? sessionIdHeader)
        {
            if (contentLength < 0)
            {
                // UE 实测：POST 不带 Content-Length（chunked/空 body）= 411 missing_content_length_header
                _methods.Enqueue("POST-no-cl");
                return (411, "Length Required",
                    """{"errorCode": "errors.com.epicgames.httpserver.missing_content_length_header", "errorMessage": ""}""",
                    null);
            }

            if (_failNonJson)
            {
                // 非 JSON 错误体场景：500 + HTML，验证客户端把状态码原样带出去（不被解析吞掉）
                _methods.Enqueue("FAIL-500-html");
                return (500, "Internal Server Error", "<html>Internal Server Error</html>", null);
            }

            if (sessionIdHeader is not null && !_validSessions.ContainsKey(sessionIdHeader))
            {
                // 模拟 UE 重启：旧 session 的 any RPC 回 404 + Unknown session id（实测 UE 原文）
                // 只拦带 session 的请求；discover/initialize 不带 session，重启后照常放行。
                _methods.Enqueue("SESSION-EXPIRED");
                int id = ExtractId(body);
                return (404, "Not Found",
                    """{"jsonrpc":"2.0","id":__ID__,"error":{"code":-32600,"message":"Unknown session id '__SID__' for '__METHOD__'; client should reinitialize"}}"""
                        .Replace("__ID__", id.ToString(System.Globalization.CultureInfo.InvariantCulture))
                        .Replace("__SID__", sessionIdHeader ?? "<none>")
                        .Replace("__METHOD__", ExtractMethod(body)),
                    null);
            }

            string rpcMethod = ExtractMethod(body);
            _methods.Enqueue(rpcMethod);

            if (rpcMethod == "server/discover")
            {
                int id = ExtractId(body);
                // UE 实测：对 2026-07-28 协议的探测回 400 + MethodNotFound，期望客户端退回首版握手
                return (400, "Bad Request",
                    """{"jsonrpc":"2.0","id":__ID__,"error":{"code":-32601,"message":"Call to unknown method \"server/discover\""}}"""
                        .Replace("__ID__", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    null);
            }

            if (rpcMethod == "initialize")
            {
                int id = ExtractId(body);
                string newSession = $"stub-session-{Interlocked.Increment(ref _issue)}";
                _validSessions[newSession] = 1;
                return (200, "OK",
                    """{"jsonrpc":"2.0","id":__ID__,"result":{"protocolVersion":"2025-11-25","capabilities":{"tools":{}},"serverInfo":{"name":"ue-stub","version":"1"},"instructions":"Stub usage notes."}}"""
                        .Replace("__ID__", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    newSession);
            }

            if (rpcMethod == "tools/list")
            {
                int id = ExtractId(body);
                return (200, "OK",
                    """{"jsonrpc":"2.0","id":__ID__,"result":{"tools":[{"name":"echo","description":"Echo the text back.","inputSchema":{"type":"object","properties":{}}},{"name":"spawn","description":"Spawn an actor.","inputSchema":{"type":"object","properties":{}}}]}}"""
                        .Replace("__ID__", id.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                    null);
            }

            // notifications/initialized 等通知：202 空响应
            return (202, "Accepted", string.Empty, null);
        }

        private static string ExtractMethod(string body)
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("method", out JsonElement m)
                ? m.GetString() ?? string.Empty
                : string.Empty;
        }

        private static int ExtractId(string body)
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.Number
                ? id.GetInt32()
                : 0;
        }

        private void Send(Stream stream, string statusLine, string contentType, string payload,
            string? sessionId = null)
        {
            byte[] body = Encoding.UTF8.GetBytes(payload);
            var header = new StringBuilder()
                .Append("HTTP/1.1 ").Append(statusLine).Append("\r\n")
                .Append("content-type: ").Append(contentType).Append("\r\n")
                .Append("content-length: ").Append(body.Length).Append("\r\n")
                .Append("connection: keep-alive\r\n");
            if (sessionId != null)
                header.Append("mcp-session-id: ").Append(sessionId).Append("\r\n");
            header.Append("\r\n");

            byte[] head = Encoding.ASCII.GetBytes(header.ToString());
            stream.Write(head, 0, head.Length);
            if (body.Length > 0) stream.Write(body, 0, body.Length);
            stream.Flush();
        }
    }
}