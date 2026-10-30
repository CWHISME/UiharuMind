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

using UiharuMind.Core.AI.Models;
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
    private readonly EApiFormat _format; //这条路径的 wire 格式(决定地址归一与响应清洗各走哪套)
    private readonly bool _isChatCompletions; //日志里要标出来:是不是聊天补全
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
        _format = ResolveFormat(address);
        _baseUri = ResolveUri(address, _format);
        _isChatCompletions = ChatCompletionUri.IsChatCompletions(_baseUri);
        _affinity = new SessionAffinityHeaders(affinityHeaders);
        _sanitizer = new ResponseSanitizer(_format, reasoningFieldObserver);
    }

    public OpenAICompatibleHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions", Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(new UriBuilder(host) { Port = port, Path = absolutePath }.Uri.ToString(), new HttpClientHandler(),
            reasoningFieldObserver, affinityHeaders)
    {
    }

    /// <summary>
    /// 从地址判断这条路径走哪种 wire 格式。
    ///
    /// 判据是<b>地址里写的什么</b>，不是模型声明的格式：handler 是按端点长期存活的，
    /// 而地址才是它实际要打的那个东西。填了 <c>/responses</c> 就是 responses 线，
    /// 否则是 chat 线。
    /// </summary>
    private static EApiFormat ResolveFormat(string address)
    {
        var builder = new UriBuilder(address);
        return ChatCompletionUri.IsResponses(builder.Uri) ? EApiFormat.Responses : EApiFormat.ChatCompletions;
    }

    /// <summary>
    /// 按格式归一地址：responses 线补 <c>/responses</c>，chat 线补 <c>/chat/completions</c>。
    /// </summary>
    private static Uri ResolveUri(string address, EApiFormat format) =>
        (format == EApiFormat.Responses ? ChatCompletionUri.ResolveResponses(address) : ChatCompletionUri.Resolve(address)).Uri;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        // 上下文只在这里取一次:写头与写日志必须是同一份值,分别取等于给「两处不一致」留了门
        string? affinity = LlmRequestScope.Current.SessionAffinity;

        // OpenAI SDK 会自行拼接标准路径，这里统一改写到用户配置的兼容端点。
        request.RequestUri = _baseUri;
        _affinity.Apply(request, affinity);

        HttpResponseMessage response = await base.SendAsync(request, cancellationToken);
        ResponseDiagnostics.LogResponse(response, _affinity.Describe(affinity), SessionAffinityHeaders.Echo(response),
            _isChatCompletions);
        await ResponseDiagnostics.LogFailureAsync(response, request.RequestUri, cancellationToken);
        return await _sanitizer.SanitizeAsync(response, cancellationToken);
    }
}
