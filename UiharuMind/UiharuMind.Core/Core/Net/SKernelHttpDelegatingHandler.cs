namespace UiharuMind.Core.Core.LLM;

class SKernelHttpDelegatingHandler : DelegatingHandler
{
    public SKernelHttpDelegatingHandler()
        : base(new HttpClientHandler())
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var newUriBuilder = new UriBuilder(request.RequestUri!)
        {
            Scheme = "http",
            Host = "127.0.0.1",
            Port = 1369
        };

        request.RequestUri = newUriBuilder.Uri;
        return base.SendAsync(request, cancellationToken);
    }
}