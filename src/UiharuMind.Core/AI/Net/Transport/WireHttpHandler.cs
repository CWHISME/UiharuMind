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
/// 模型端点的 HTTP 层，只做四件<b>与协议无关</b>的事：改地址、写亲和头、记状态日志、把响应交给清洗。四处归口：
/// <see cref="WireProtocol"/>（地址归一与「这条线要不要洗」）、<see cref="SessionAffinityHeaders"/>（亲和头与其回显）、
/// <see cref="Diagnostics.ResponseDiagnostics"/>（状态行与失败正文两行日志）、<see cref="ResponseSanitizer"/>（修响应）。
/// 请求体的改写与请求日志不在这条链路上，在 <see cref="Request.OpenAICompatibleRequestPolicy"/>（重试之前，每次调用只做一次）。
/// </summary>
class WireHttpHandler : DelegatingHandler
{
    private readonly Uri _baseUri; //真正要打的那个地址
    private readonly WireProtocol _protocol; //这条端点的协议(决定地址归一与响应清洗各走哪套)
    private readonly SessionAffinityHeaders _affinity; //本端点的亲和头
    private readonly ResponseSanitizer _sanitizer; //响应清洗（流式与一次性两种形状）

    public WireHttpHandler(string address = "http://127.0.0.1:1369/v1/chat/completions",
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null,
        EApiFormat? format = null,
        bool sendsAffinity = false)
        : this(address, new HttpClientHandler(), reasoningFieldObserver, affinityHeaders, format, sendsAffinity)
    {
    }

    /// <summary>
    /// 指定内层处理器（测试用：替掉真实网络）
    /// </summary>
    /// <param name="address">端点地址</param>
    /// <param name="inner">内层处理器</param>
    /// <param name="reasoningFieldObserver">思考字段观测回调（可选，观测到 origin 字段名时调用）</param>
    /// <param name="affinityHeaders">供应商要追加的亲和头名；公共头与协议头另有来源，不在这里</param>
    /// <param name="format">
    /// 模型声明的 wire 格式。<b>有模型就一定要传</b>：它是唯一权威来源，
    /// 不传才退到按地址反查（只给拿不到模型的调用方用：本地服务、模型目录、状态探测）。
    /// 两条来源并存时必须以模型为准——用户把地址填成 <c>/v1</c> 时按地址猜不出协议，
    /// 退到 chat 就会把 messages 形状的请求体打到 chat 端点上
    /// </param>
    /// <param name="sendsAffinity">这家发不发会话亲和；本地服务与显式关掉的供应商传 false</param>
    internal WireHttpHandler(string address, HttpMessageHandler inner,
        Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null,
        EApiFormat? format = null,
        bool sendsAffinity = false) : base(inner)
    {
        _protocol = format is { } declared
            ? WireProtocols.For(declared)
            : WireProtocols.Detect(new UriBuilder(address).Uri) ?? WireProtocols.Chat;
        _baseUri = _protocol.Resolve(address).Uri;
        // 三层在这里合流：公共头默认发，协议头按线补，供应商只补自己独有的
        _affinity = new SessionAffinityHeaders(sendsAffinity, _protocol.SessionHeaders, affinityHeaders);
        _sanitizer = new ResponseSanitizer(_protocol.SanitizesResponse, reasoningFieldObserver);
    }

    public WireHttpHandler(string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions", Action<string>? reasoningFieldObserver = null,
        IReadOnlyList<string>? affinityHeaders = null)
        : this(new UriBuilder(host) { Port = port, Path = absolutePath }.Uri.ToString(), new HttpClientHandler(),
            reasoningFieldObserver, affinityHeaders)
    {
    }

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
            _protocol.SanitizesResponse);
        await ResponseDiagnostics.LogFailureAsync(response, request.RequestUri, cancellationToken);
        return await _sanitizer.SanitizeAsync(response, cancellationToken);
    }
}
