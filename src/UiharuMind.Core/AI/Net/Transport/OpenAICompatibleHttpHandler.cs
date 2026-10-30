/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using UiharuMind.Core.AI.Net.Diagnostics;
using UiharuMind.Core.AI.Net.Response;

namespace UiharuMind.Core.AI.Net.Transport;

/// <summary>
/// OpenAI 兼容端点的 HTTP 层，只做三件事：改地址、写亲和头、把响应交给清洗。四处归口：
/// <see cref="ChatCompletionUri"/>（地址归一）、<see cref="SessionAffinityHeaders"/>（亲和头与其回显）、
/// <see cref="Diagnostics.ResponseDiagnostics"/>（状态行与失败正文两行日志）、<see cref="ResponseSanitizer"/>（修响应）。
/// 请求体的改写与请求日志不在这条链路上，在 <see cref="Request.OpenAICompatibleRequestPolicy"/>（重试之前，每次调用只做一次）。
/// </summary>
class OpenAICompatibleHttpHandler : DelegatingHandler
{
    private readonly Uri _baseUri; //真正要打的那个地址
    private readonly bool _isChatCompletions; //只有聊天补全的响应被清洗(日志里要标出来)
    private readonly SessionAffinityHeaders _affinity; //本端点的亲和头
    private readonly ResponseSanitizer _sanitizer; //响应清洗（流式与一次性两种形状）

    public OpenAICompatibleHttpHandler(string address = "http://127.0.0.1:1369/v1/chat/completions",
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(address, new HttpClientHandler(), reasoningFieldObserver, affinityHeaders)
    {
    }

    /// <summary>
    /// 指定内层处理器（测试用：替掉真实网络）
    /// </summary>
    /// <param name="address">端点地址</param>
    /// <param name="inner">内层处理器</param>
    /// <param name="reasoningFieldObserver">思考字段观测回调（可选，观测到 origin 字段名时调用）</param>
    /// <param name="affinityHeaders">会话亲和要写的头名；空或 null 表示这家网关不认，一个都不发</param>
    internal OpenAICompatibleHttpHandler(string address, HttpMessageHandler inner,
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null) : base(inner)
    {
        _baseUri = ChatCompletionUri.Resolve(address).Uri;
        _isChatCompletions = ChatCompletionUri.IsChatCompletions(_baseUri);
        _affinity = new SessionAffinityHeaders(affinityHeaders);
        _sanitizer = new ResponseSanitizer(_isChatCompletions, reasoningFieldObserver);
    }

    public OpenAICompatibleHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions", Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(new UriBuilder(host) { Port = port, Path = absolutePath }.Uri.ToString(), new HttpClientHandler(),
            reasoningFieldObserver, affinityHeaders)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // OpenAI SDK 会自行拼接标准路径，这里统一改写到用户配置的兼容端点。
        request.RequestUri = _baseUri;
        _affinity.Apply(request);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        ResponseDiagnostics.LogResponse(response, _affinity.Describe(), SessionAffinityHeaders.Echo(response),
            _isChatCompletions);
        await ResponseDiagnostics.LogFailureAsync(response, request.RequestUri, cancellationToken);
        return await _sanitizer.SanitizeAsync(response, cancellationToken);
    }
}
