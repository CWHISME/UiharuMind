using UiharuMind.Core.AI.Execution.Tools.WebTools;

namespace UiharuMind.Core.Tests.Execution;

/// <summary>
/// 钉死正文读取兜底链的判定:谁受理哪个地址、什么算读到了、什么算空壳。
/// 都是纯函数级的判断,不发真实请求。
/// </summary>
public class WebFetchReaderTests
{
    [Fact]
    public void Firecrawl_ParsesMarkdown()
    {
        PageReadResult result = FirecrawlPageReader.Parse("""{"data":{"markdown":"# Title\ntext"}}""");

        Assert.Equal("# Title\ntext", result.Content);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Firecrawl_EmptyOrMissingData_Fails()
    {
        Assert.Null(FirecrawlPageReader.Parse("""{"data":{"markdown":""}}""").Content);
        Assert.NotNull(FirecrawlPageReader.Parse("""{"foo":"bar"}""").Error);
    }

    /// <summary>
    /// 读页与搜索共用 <see cref="FirecrawlClient"/> 出口,业务错误必须同样抛出来进熔断换源——
    /// 不能像从前那样被 <see cref="FirecrawlPageReader.Parse"/> 吞成"no data"再落回直连链,
    /// 错误原文丢了、Firecrawl 也不被熔断。
    /// </summary>
    [Fact]
    public void Firecrawl_BusinessError_ThrowsInsteadOfSilentFallback()
    {
        var ex = Assert.Throws<HttpRequestException>(
            () => FirecrawlClient.EnsureSuccess("""{"success":false,"error":"rate limited"}"""));
        Assert.Contains("rate limited", ex.Message);
    }

    /// <summary>无 success 字段或 success 为 true 都不算业务错误</summary>
    [Fact]
    public void Firecrawl_NoBusinessError_DoesNotThrow()
    {
        FirecrawlClient.EnsureSuccess("""{"data":{"markdown":"# Title"}}""");
        FirecrawlClient.EnsureSuccess("""{"success":true,"data":{"web":[]}}""");
    }

    /// <summary>
    /// 内网地址一律不许出门:Firecrawl 在它自己的机器上解析,既读不到,还把地址泄露了
    /// </summary>
    [Theory]
    [InlineData("http://localhost:8080/admin")]
    [InlineData("http://127.0.0.1/")]
    [InlineData("http://[::1]:3000/")]
    [InlineData("http://10.0.0.5/")]
    [InlineData("http://172.16.3.9/")]
    [InlineData("http://172.31.255.1/")]
    [InlineData("http://192.168.1.1/")]
    [InlineData("http://169.254.169.254/latest/meta-data")]
    [InlineData("http://100.101.102.103/")]
    [InlineData("http://nas/photos")]
    [InlineData("http://gitlab.internal/repo")]
    [InlineData("http://printer.local/")]
    public void PrivateHosts_AreRejectedByFirecrawl(string url)
    {
        Assert.True(WebShared.IsLocalOrPrivateHost(url));
        Assert.False(new FirecrawlPageReader().CanRead(url));
    }

    [Theory]
    [InlineData("https://example.com/a")]
    [InlineData("https://docs.firecrawl.dev/")]
    [InlineData("http://172.32.0.1/")] //刚好落在 172.16/12 之外
    [InlineData("http://8.8.8.8/")]
    public void PublicHosts_AreAcceptedByFirecrawl(string url)
    {
        Assert.False(WebShared.IsLocalOrPrivateHost(url));
        Assert.True(new FirecrawlPageReader().CanRead(url));
    }

    /// <summary>直连读取器不挑地址,内网正是靠它兜底</summary>
    [Fact]
    public void DirectReader_AcceptsPrivateHosts()
    {
        Assert.True(((IPageReader)new DirectPageReader()).CanRead("http://192.168.1.1/"));
    }

    /// <summary>llms.txt 读取器只受理能拼出同源地址的 http/https</summary>
    [Theory]
    [InlineData("https://youtrack.jetbrains.com/issue/RIDER-71303", "https://youtrack.jetbrains.com/llms.txt")]
    [InlineData("http://example.com/a?b=1", "http://example.com/llms.txt")]
    public void Llmstxt_BuildsSameOriginUrl(string url, string expected)
    {
        Assert.Equal(expected, LlmstxtPageReader.BuildLlmstxtUrl(url));
    }

    [Theory]
    [InlineData("ftp://example.com/a")]
    [InlineData("not a url")]
    [InlineData("")]
    public void Llmstxt_UnsupportedUrl_IsNotHandled(string url)
    {
        Assert.Null(LlmstxtPageReader.BuildLlmstxtUrl(url));
        Assert.False(((IPageReader)new LlmstxtPageReader()).CanRead(url));
    }

    /// <summary>
    /// 截断流读满上限即 EOF 而非抛异常:大 HTML 页面整体报废会让
    /// WebFetchTool 的 64KB 头尾骨架机制够不着;截断可用与纯文本分支同口径。
    /// </summary>
    [Fact]
    public void BoundedStream_StopsAtCap_ReturnsEof()
    {
        using MemoryStream inner = new(Enumerable.Repeat((byte)0x41, 100).ToArray()); //100 字节
        DirectPageReader.BoundedStream stream = new(inner, cap: 40);

        byte[] buffer = new byte[100];
        int first = stream.Read(buffer, 0, buffer.Length);
        int second = stream.Read(buffer, 0, buffer.Length);

        Assert.Equal(40, first); //只暴露前 cap 字节
        Assert.Equal(0, second); //读满即 EOF,不抛异常
    }

    /// <summary>
    /// 原样取回的内容再短也算数:一个 80 字节的接口响应是正确结果,不是空壳;
    /// 抽取来的正文才适用"太短八成没抽到"的判断。
    /// </summary>
    [Fact]
    public void ExactContent_IsNotSubjectToMinLength()
    {
        PageReadResult exact = PageReadResult.Exact("""{"ok":true}""");
        PageReadResult extracted = PageReadResult.Ok("""{"ok":true}""");

        Assert.True(exact.IsExact);
        Assert.False(extracted.IsExact);
    }
}
