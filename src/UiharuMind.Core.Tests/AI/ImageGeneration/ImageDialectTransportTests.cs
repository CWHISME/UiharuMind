using System.Net;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.ImageGeneration.Dialects;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

/// <summary>
/// 发送层的失败分类：同一个「没出图」，有的该换家，有的换了会重复扣费
/// </summary>
public class ImageDialectTransportTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return send(request, ct);
        }
    }

    private static ImageModelInfo Model(int timeoutSeconds = 30) => new()
    {
        Name = "m", Dialect = EImageDialect.SenseNova, Endpoint = "https://example.com/v1", ModelId = "x",
        ApiKey = "sk-test", TimeoutSeconds = timeoutSeconds,
    };

    private static Task<ImageOutcome> Run(StubHandler handler, ImageModelInfo? model = null) =>
        new SenseNovaImageDialect(new HttpClient(handler))
            .GenerateAsync(model ?? Model(), new ImageRequest("cat", [], null), TestContext.Current.CancellationToken);

    private static StubHandler Respond(HttpStatusCode status, string body) =>
        new((_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    [Fact]
    public async Task Success_SendsBearerAndParsesImages()
    {
        StubHandler handler = Respond(HttpStatusCode.OK, """{"data":[{"b64_json":"AQI="}]}""");

        ImageOutcome outcome = await Run(handler);

        Assert.Null(outcome.Failure);
        Assert.Equal("sk-test", handler.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ContentRejection_StopsWithServerMessage()
    {
        ImageOutcome outcome = await Run(Respond(HttpStatusCode.BadRequest,
            """{"error":{"type":"failed_precondition_error","code":"9","message":"safety check failed"}}"""));

        Assert.Equal(EImageFailureKind.Rejected, outcome.Failure!.Kind);
        Assert.Contains("failed_precondition_error: safety check failed", outcome.Failure.Message);
    }

    [Fact]
    public async Task ConnectionRefused_IsUnavailable()
    {
        ImageOutcome outcome = await Run(new StubHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ConnectionError, "refused")));

        Assert.Equal(EImageFailureKind.Unavailable, outcome.Failure!.Kind);
    }

    [Fact]
    public async Task ResponseCutOff_MayBeCharged()
    {
        ImageOutcome outcome = await Run(new StubHandler((_, _) =>
            throw new HttpRequestException(HttpRequestError.ResponseEnded, "ended")));

        Assert.Equal(EImageFailureKind.MaybeCharged, outcome.Failure!.Kind);
    }

    [Fact]
    public async Task Timeout_MayBeCharged()
    {
        ImageOutcome outcome = await Run(new StubHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }), Model(timeoutSeconds: 1));

        Assert.Equal(EImageFailureKind.MaybeCharged, outcome.Failure!.Kind);
        Assert.Contains("billed", outcome.Failure.Message);
    }

    [Fact]
    public async Task UserCancel_Throws()
    {
        using CancellationTokenSource cts = new();
        cts.Cancel();
        StubHandler handler = new((_, ct) => Task.FromCanceled<HttpResponseMessage>(ct));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new SenseNovaImageDialect(new HttpClient(handler)).GenerateAsync(Model(), new ImageRequest("cat", [], null), cts.Token));
    }

    [Fact]
    public async Task BrokenExtraBody_IsUnavailableWithoutSending()
    {
        bool sent = false;
        ImageModelInfo model = Model();
        model.ExtraBody = "{not json";

        ImageOutcome outcome = await Run(new StubHandler((_, _) =>
        {
            sent = true;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }), model);

        Assert.Equal(EImageFailureKind.Unavailable, outcome.Failure!.Kind);
        Assert.False(sent);
    }
}
