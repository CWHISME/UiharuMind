using System.Net;
using UiharuMind.Core.Core.LLM;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 失败诊断日志：头采集按子串命中（各家名字都不同），以及正文只抹 base64、不截断。
/// 成功路径不打日志——配额头探测的结论已经有了，那行日志只剩噪音。
/// </summary>
public class OpenAICompatibleFailureLogTests
{
    [Fact]
    public void CollectsRateLimitAndRequestIdHeaders()
    {
        using HttpResponseMessage response = new(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining-tokens", "0");
        response.Headers.TryAddWithoutValidation("Retry-After", "20");
        response.Headers.TryAddWithoutValidation("x-tc-requestid", "abc-123");
        response.Headers.TryAddWithoutValidation("server", "nginx");

        string? diagnostics = OpenAICompatibleHttpHandler.FormatDiagnosticHeaders(response);

        Assert.NotNull(diagnostics);
        Assert.Contains("x-ratelimit-remaining-tokens=0", diagnostics);
        Assert.Contains("Retry-After=20", diagnostics);
        Assert.Contains("x-tc-requestid=abc-123", diagnostics);
        Assert.DoesNotContain("nginx", diagnostics);
    }

    [Fact]
    public void CollectsFromContentHeadersToo()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK)
        {
            Content = new StringContent("{}"),
        };
        response.Content.Headers.TryAddWithoutValidation("x-ratelimit-limit-requests", "60");

        string? diagnostics = OpenAICompatibleHttpHandler.FormatDiagnosticHeaders(response);

        Assert.Equal("x-ratelimit-limit-requests=60", diagnostics);
    }

    [Fact]
    public void NoQuotaHeaders_ReturnsNull()
    {
        using HttpResponseMessage response = new(HttpStatusCode.OK);
        response.Headers.TryAddWithoutValidation("server", "nginx");

        Assert.Null(OpenAICompatibleHttpHandler.FormatDiagnosticHeaders(response));
    }

    /// <summary>
    /// 日志正文不截断，只抹 base64。撑爆日志的是内联附件而不是正文长度，
    /// 一刀切截断会把提示词、工具定义和参数一起看不见——那正是这条日志存在的意义。
    /// </summary>
    [Fact]
    public void DataUrlPayload_IsRedactedButThePrefixStays()
    {
        string body = "{\"image_url\":{\"url\":\"data:image/png;base64," + new string('A', 2000) + "\"}}";

        string logged = OpenAICompatibleHttpHandler.ForLog(body);

        Assert.Contains("data:image/png;base64,", logged); //还看得出这里挂过一张 png
        Assert.Contains("base64 chars>", logged);
        Assert.DoesNotContain(new string('A', 600), logged);
        Assert.True(logged.Length < 200);
    }

    [Fact]
    public void BareBase64Value_IsRedacted()
    {
        string body = "{\"data\":\"" + new string('Q', 3000) + "\"}";

        string logged = OpenAICompatibleHttpHandler.ForLog(body);

        Assert.Contains("base64 chars>", logged);
        Assert.DoesNotContain(new string('Q', 600), logged);
    }

    [Fact]
    public void PromptTextAndParameters_SurviveIntact()
    {
        const string body =
            """{"temperature":0.5,"top_p":0.8,"messages":[{"role":"system","content":"# Task 你是Uiharu，具备活泼的性格。"}],"tools":[{"function":{"name":"run_shell"}}]}""";

        string logged = OpenAICompatibleHttpHandler.ForLog(body);

        //内容一个字都不能少;中文必须是中文,不能是 \uXXXX——那样日志基本没法读
        Assert.Contains("# Task 你是Uiharu，具备活泼的性格。", logged);
        Assert.Contains("run_shell", logged);
        Assert.Contains("0.5", logged);
        Assert.DoesNotContain("\\u", logged);
    }

    [Fact]
    public void JsonIsExpandedNotMinified()
    {
        //发出去的是压缩过的单行 JSON,直接写进日志就是挤成一坨的一大段
        const string body = """{"a":1,"b":{"c":2}}""";

        string logged = OpenAICompatibleHttpHandler.ForLog(body);

        Assert.Contains("\n", logged);
    }

    [Fact]
    public void NonJsonBody_IsLeftAlone()
    {
        //错误响应未必是 JSON,格式化失败不该影响任何事
        const string body = "upstream connect error";

        Assert.Equal(body, OpenAICompatibleHttpHandler.ForLog(body));
    }

    [Fact]
    public void ShortBase64LikeString_IsLeftAlone()
    {
        //短的可能是真内容(id、哈希),抹掉反而丢信息
        string logged = OpenAICompatibleHttpHandler.ForLog("""{"id":"chatcmpl-abc123XYZ"}""");

        Assert.Contains("chatcmpl-abc123XYZ", logged);
    }
}
