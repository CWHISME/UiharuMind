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
using System.Text.RegularExpressions;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.LLM;

class SKernelHttpDelegatingHandler : DelegatingHandler
{
    private readonly Uri _baseUri;

    public SKernelHttpDelegatingHandler(string address = "http://127.0.0.1:1369/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder(address);
        _baseUri = newUriBuilder.Uri;
    }

    public SKernelHttpDelegatingHandler(string host = "http://127.0.0.1", int port = 1369,
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
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.RequestUri = _baseUri;
        // var mediaType = request.Content!.Headers!.ContentType!.MediaType;
        if (request.Content!.Headers!.ContentType!.MediaType == "application/json")
        {
            var content = Regex.Unescape(await request.Content!.ReadAsStringAsync(cancellationToken));
            Log.Debug($"Kernel Send : {content}");
            // request.Content = new StringContent(content, Encoding.UTF8, mediaType);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}