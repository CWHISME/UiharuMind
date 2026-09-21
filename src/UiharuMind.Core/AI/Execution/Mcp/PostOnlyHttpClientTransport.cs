/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace UiharuMind.Core.AI.Execution.Mcp;

/// <summary>
/// 纯 POST JSON-RPC 传输 —— 给只实现 POST（不实现 GET/SSE）的 MCP server 用。
/// 每次请求都 POST 同一 endpoint，响应是 application/json（或 text/event-stream 单帧），
/// 不做 Streamable HTTP 的 GET 数据流，也不做 SSE 回退。
/// </summary>
internal sealed class PostOnlyHttpTransport : TransportBase
{
    /// 与 SDK 默认一致的序列化选项，仅补上 JsonRpcMessage 的转换器
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonRpcMessage.Converter() },
    };

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
            Content = new StringContent(JsonSerializer.Serialize(message, SerializerOptions),
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
            throw new HttpRequestException(
                $"Response status code does not indicate success: {(int)response.StatusCode} ({response.StatusCode}).");
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

        JsonRpcMessage? reply = JsonSerializer.Deserialize<JsonRpcMessage>(payload, SerializerOptions);
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

    public override ValueTask DisposeAsync() => default;
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
