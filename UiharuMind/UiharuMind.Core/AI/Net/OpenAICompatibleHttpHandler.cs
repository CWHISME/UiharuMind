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
using UiharuMind.Core.AI.Interfaces;
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

            var extraParams = _model?.GetExtraParams();
            if (extraParams != null)
            {
                var jsonNode = JsonNode.Parse(jsonContent)?.AsObject();

                if (jsonNode != null)
                {
                    // jsonNode["thinking"] = new JsonObject { ["type"] = "disabled" };
                    jsonNode.Add((KeyValuePair<string, JsonNode?>)extraParams);
                    jsonContent = jsonNode.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
                    request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
                }
            }

            Log.Debug($"OpenAI-compatible request: {Regex.Unescape(jsonContent)}");
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
