using System.Net;
using UiharuMind.Core.AI.Status;
using UiharuMind.Core.AI.Status.Providers.CommandCode;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// CC 取数源：端点路径、鉴权头、401 转降级快照、429 抛出去让服务层退避。
/// 用假 handler 替掉网络，不依赖真 key。
/// </summary>
public class CommandCodeStatusSourceTests
{
    private static ProviderStatusContext Context() =>
        new(typeof(RemoteCommandCodeModelConfig), "https://api.commandcode.ai", "test-key",
            "deepseek-v4-flash", "DeepSeek V4 Flash");

    // 实测形状（fixtures/commandcode-credits-empty.json 是原文；这里补上窗口，字段名仍是假拟）
    private const string CreditsSample = """
        {
          "credits": { "monthlyCredits": 12.4, "purchasedCredits": 0, "freeCredits": 0 },
          "windowLimits": {
            "limited": true,
            "exceeded": null,
            "fiveHour": { "used": 3.2, "limit": 10 },
            "weekly": { "used": 41, "limit": 100 }
          }
        }
        """;

    [Fact]
    public async Task Fetch_UsesAlphaPathsAndBearerToken()
    {
        List<string> requested = [];
        List<string?> authorizations = [];
        ProviderStatusSourceWith((request) =>
        {
            requested.Add(request.RequestUri!.AbsolutePath);
            authorizations.Add(request.Headers.Authorization?.ToString());
            return request.RequestUri!.AbsolutePath.EndsWith("/whoami")
                ? Json("""{ "plan": { "name": "Pro" } }""")
                : Json(CreditsSample);
        }, out CommandCodeStatusSource source);

        ProviderStatusSnapshot? snapshot = await source.FetchAsync(Context(), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Contains("/alpha/billing/credits", requested);
        Assert.Contains("/alpha/whoami", requested);
        Assert.All(authorizations, header => Assert.Equal("Bearer test-key", header));
        Assert.Equal(2, snapshot!.Meters.Count);
        Assert.Equal("Pro", snapshot.Balance?.PlanName);
    }

    [Fact]
    public async Task Fetch_WhoamiFailure_KeepsTheNumbers()
    {
        ProviderStatusSourceWith((request) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/whoami")) return new HttpResponseMessage(HttpStatusCode.NotFound);
            return Json(CreditsSample);
        }, out CommandCodeStatusSource source);

        ProviderStatusSnapshot? snapshot = await source.FetchAsync(Context(), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Meters.Count);
        Assert.Null(snapshot.Balance?.PlanName);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Fetch_CredentialFailure_BecomesDegradedSnapshot(HttpStatusCode status)
    {
        ProviderStatusSourceWith(_ => new HttpResponseMessage(status),
            out CommandCodeStatusSource source);

        ProviderStatusSnapshot? snapshot = await source.FetchAsync(Context(), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Meters);
        Assert.Equal(EProviderNoticeKind.CredentialInvalid, Assert.Single(snapshot.AccountNotices).Kind);
    }

    [Fact]
    public async Task Fetch_RateLimited_ThrowsSoTheServiceCanBackOff()
    {
        ProviderStatusSourceWith(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests),
            out CommandCodeStatusSource source);

        await Assert.ThrowsAsync<HttpRequestException>(() => source.FetchAsync(Context(), CancellationToken.None));
    }

    [Fact]
    public async Task Fetch_WithoutKeyOrBase_DoesNothing()
    {
        FakeHandler handler = new(_ => Json(CreditsSample));
        CommandCodeStatusSource source = new(handler);

        Assert.Null(await source.FetchAsync(
            new ProviderStatusContext(typeof(RemoteCommandCodeModelConfig), "", "", "m", "m"), CancellationToken.None));
        Assert.Null(await source.FetchAsync(
            new ProviderStatusContext(typeof(RemoteCommandCodeModelConfig), "https://api.commandcode.ai", "", "m", "m"),
            CancellationToken.None));
        Assert.Equal(0, handler.Calls);
    }

    private static void ProviderStatusSourceWith(Func<HttpRequestMessage, HttpResponseMessage> respond,
        out CommandCodeStatusSource source)
    {
        source = new CommandCodeStatusSource(new FakeHandler(respond));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(respond(request));
        }
    }
}
