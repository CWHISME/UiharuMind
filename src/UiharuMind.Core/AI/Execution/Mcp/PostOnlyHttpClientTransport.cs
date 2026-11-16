/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 纯 POST JSON-RPC 传输 —— 给只实现 POST（不实现 GET/SSE）的 MCP server 用。
/// 每次请求都 POST 同一 endpoint，响应是 application/json（或 text/event-stream 单帧），
/// 不做 Streamable HTTP 的 GET 数据流，也不做 SSE 回退。
/// </summary>
internal sealed class PostOnlyHttpTransport : TransportBase
{
    private static readonly JsonTypeInfo<JsonRpcMessage> MessageInfo = McpJsonUtilities.DefaultOptions.GetTypeInfo<JsonRpcMessage>();

    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly IReadOnlyDictionary<string, string>? _headers;

    public PostOnlyHttpTransport(string name, Uri endpoint, HttpClient httpClient,
        IReadOnlyDictionary<string, string>? headers = null)
        : base(name, null)
    {
        _endpoint = endpoint;
        _httpClient = httpClient;
        _headers = headers;
    }

    /// <summary>把一条 JSON-RPC 消息 POST 出去，把响应写回消息通道</summary>
    public override Task SendMessageAsync(JsonRpcMessage message, CancellationToken cancellationToken = default)
        => SendMessageCoreAsync(message, cancellationToken);

    private async Task SendMessageCoreAsync(JsonRpcMessage message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(message, MessageInfo),
                System.Text.Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("text/event-stream"));

        if (_headers != null)
        {
            foreach (KeyValuePair<string, string> kv in _headers)
                request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }

        if (SessionId != null)
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", SessionId);

        using HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 带 Mcp-Session-Id 的请求收到 404 = 会话过期（UE 编辑器重启后最常见：session 是
            // 进程内存态，随重启丢失）。规范要求客户端重建会话——这里必须抛专型的
            // ClientTransportClosedException：上层（McpBridge.IsConnectionFailure）只认它为
            // 「连接死了可丢连接重连」；若抛裸 404 HttpRequestException，会被当成业务 4xx
            // 直接放弃，客户端就拿旧 session 死磕（实测：SDK 只把 HttpRequestException(404)
            // 传播出来，通道完成的信号进不了调用链）。同时完成通道，让 Completion 也能感知。
            if (response.StatusCode == HttpStatusCode.NotFound && SessionId is not null)
            {
                // 详情用 IOException 而非 McpException 承载：McpException 在 McpBridge 的
                // IsConnectionFailure 里是「server 正常回话、重连救不了」的立即否定信号，
                // 会话过期恰恰是重连能救的（走 ClientTransportClosedException 判定）。实测
                // 若 InnerException 是 McpException，IsConnectionFailure 直接 false，上层
                // 不会丢连接重连。
                var expired = new ClientTransportClosedException(new HttpClientCompletionDetails
                {
                    HttpStatusCode = HttpStatusCode.NotFound,
                    Exception = new IOException(
                        "The server returned HTTP 404 for a request with an Mcp-Session-Id, indicating the session has expired. " +
                        "To continue, create a new client session."),
                });
                SetDisconnected(expired);
                throw expired;
            }

            // 非 2xx 一律抛带 StatusCode 的 HttpRequestException：SDK 的 discover 探测按状态码
            // 识别 400/404 为「server 不支持新协议探测」，自动回退 initialize（真机 UE 实测
            // 可行，探针 0.3s 内回退并拿到 11 个工具）。不带 StatusCode 的旧写法让 SDK 认不出
            // 可回退的拒绝，discover 穿异常导致连接失败——不要在此处把 JSON-RPC 错误体当响应
            // 送达通道：那既依赖 server 回显 id，又会把非 JSON-RPC 错误解析成 JsonException 丢状态码。
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).",
                null, response.StatusCode);
        }

        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // initialize 的会话 id 来自响应头，后续请求带上
        if (message is JsonRpcRequest { Method: "initialize" } &&
            response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? ids))
        {
            SessionId = System.Linq.Enumerable.FirstOrDefault(ids);
        }

        string payload = body;
        if (response.Content.Headers.ContentType?.MediaType == "text/event-stream")
            payload = ExtractSseData(body);

        if (string.IsNullOrWhiteSpace(payload))
            return;

        JsonRpcMessage? reply = JsonSerializer.Deserialize(payload, MessageInfo);
        if (reply != null)
        {
            // WriteMessageAsync 只在 IsConnected 时才写入通道，必须先置为已连接
            SetConnected();
            await WriteMessageAsync(reply, cancellationToken).ConfigureAwait(false);
        }
    }

    /// 从 SSE 响应里取出第一段 data: 行的内容；纯 POST 应答通常单帧
    private static string ExtractSseData(string body)
    {
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();
            if (trimmed.StartsWith("data:", System.StringComparison.Ordinal))
                return trimmed[5..].Trim();
        }

        return string.Empty;
    }

    public override ValueTask DisposeAsync()
    {
        // SDK 的 McpClient.DisposeAsync 会等传输通道完成（MessageReader.Completion，由
        // SetDisconnected 结束）。此前空实现让通道永不结束，dispose 永久挂起（实测连 UE 后
        // 一 close 就卡死）。这里一并释放自建的 HttpClient——重连时工厂会新 new 一个，
        // 不释放则每次重连漏 socket（评审 P1-4，桩侧实测每轮净增 2 条连接）。
        SetDisconnected();
        _httpClient.Dispose();
        return default;
    }
}

/// <summary>
/// <see cref="PostOnlyHttpTransport"/> 的 <see cref="IClientTransport"/> 工厂包装，
/// 使它能作为 <c>McpClient.CreateAsync</c> 的传输传入（ConnectAsync 返回 ITransport）。
/// </summary>
internal sealed class PostOnlyClientTransport : IClientTransport
{
    private readonly string _name;
    private readonly Uri _endpoint;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyDictionary<string, string>? _headers;

    public string Name => _name;

    public PostOnlyClientTransport(string name, Uri endpoint, HttpClient httpClient,
        IReadOnlyDictionary<string, string>? headers = null)
    {
        _name = name;
        _endpoint = endpoint;
        _httpClient = httpClient;
        _headers = headers;
    }

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ITransport>(
            new PostOnlyHttpTransport(_name, _endpoint, _httpClient, _headers));
    }
}
