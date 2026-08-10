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

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using UiharuMind.Core.AI;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Net;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.LLM;

class OpenAICompatibleHttpHandler : DelegatingHandler
{
    private readonly Uri _baseUri;
    private readonly ILlmModel? _model;

    public OpenAICompatibleHttpHandler(ILlmModel? model, string address = "http://127.0.0.1:1369/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = CreateChatCompletionUri(address);
        _baseUri = newUriBuilder.Uri;
        _model = model;
    }

    public OpenAICompatibleHttpHandler(ILlmModel? model, string host = "http://127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder(host)
        {
            // Scheme = "http",
            // Host = host,
            Port = port,
            Path = absolutePath
        };
        _baseUri = newUriBuilder.Uri;
        _model = model;
    }

    private static UriBuilder CreateChatCompletionUri(string address)
    {
        var builder = new UriBuilder(address);
        string path = builder.Path.TrimEnd('/');
        // 远程配置既允许填写完整接口，也允许只填写 OpenAI-compatible 的服务根路径。
        if (path.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            builder.Path = path + "/chat/completions";
        else if (string.IsNullOrEmpty(path))
            builder.Path = "/v1/chat/completions";
        return builder;
    }

    // protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
    //     CancellationToken cancellationToken)
    // {
    //     request.RequestUri = _baseUri;
    //     // var mediaType = request.Content!.Headers!.ContentType!.MediaType;
    //     if (request.Content!.Headers!.ContentType!.MediaType == "application/json")
    //     {
    //         var content = Regex.Unescape(await request.Content!.ReadAsStringAsync(cancellationToken));
    //         Log.Debug($"OpenAI-compatible request: {content}");
    //         // request.Content = new StringContent(content, Encoding.UTF8, mediaType);
    //     }
    //
    //     return await base.SendAsync(request, cancellationToken);
    // }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // OpenAI SDK 会自行拼接标准路径，这里统一改写到用户配置的兼容端点。
        request.RequestUri = _baseUri;
        if (request.Method == HttpMethod.Post && request.Content != null)
        {
            var jsonContent = await request.Content.ReadAsStringAsync(cancellationToken);

            var extraParams = _model?.GetExtraParams(LlmRequestContext.ThinkingMode);
            if (extraParams is { Count: > 0 })
            {
                var jsonNode = JsonNode.Parse(jsonContent)?.AsObject();

                if (jsonNode != null)
                {
                    foreach (var extraParam in extraParams)
                    {
                        jsonNode[extraParam.Key] = extraParam.Value;
                    }

                    jsonContent = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                }
            }

            Log.Debug($"OpenAI-compatible request: {Regex.Unescape(jsonContent)}");
        }

        var response = await base.SendAsync(request, cancellationToken);
        return await SanitizeResponseAsync(response, cancellationToken);
    }

    // 部分兼容服务(如商汤 Sensenova)会返回空的/非标准的 finish_reason，OpenAI SDK 解析枚举时会直接抛异常
    private async Task<HttpResponseMessage> SanitizeResponseAsync(HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (!_baseUri.AbsolutePath.Contains("chat/completions", StringComparison.OrdinalIgnoreCase)) return response;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType == "text/event-stream")
        {
            response.Content = new SseSanitizingContent(response.Content);
        }
        else if (mediaType == "application/json")
        {
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var fixedJson = OpenAiCompatibleResponseFixer.FixJson(json);
            if (fixedJson != null)
            {
                var contentType = response.Content.Headers.ContentType;
                response.Content = new StringContent(fixedJson, Encoding.UTF8, "application/json");
                if (contentType != null) response.Content.Headers.ContentType = contentType;
            }
        }

        return response;
    }
}
