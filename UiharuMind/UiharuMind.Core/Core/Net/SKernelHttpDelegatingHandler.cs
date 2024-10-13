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

namespace UiharuMind.Core.Core.LLM;

class SKernelHttpDelegatingHandler : DelegatingHandler
{
    private readonly Uri _baseUri;

    public SKernelHttpDelegatingHandler(string host = "127.0.0.1", int port = 1369,
        string absolutePath = "/v1/chat/completions")
        : base(new HttpClientHandler())
    {
        var newUriBuilder = new UriBuilder()
        {
            Scheme = "http",
            Host = host,
            Port = port,
            Path = absolutePath
        };
        _baseUri = newUriBuilder.Uri;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.RequestUri = _baseUri;
        return base.SendAsync(request, cancellationToken);
    }
}