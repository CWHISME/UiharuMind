using System.Text;
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

    /// <summary>
    /// Direct 抽取 HTML 正文后交给 MarkdownCleaner:标题、段落、链接、列表保留结构,
    /// 而不是 TextContent 式的段落粘连纯文本。
    /// </summary>
    [Fact]
    public void DirectReader_CleanMarkup_PreservesStructure()
    {
        string markdown = DirectPageReader.CleanMarkup(
            "<h2>标题</h2><p>第一段<b>加粗</b>。</p><p>第二段<a href=\"https://x.com\">链接</a>。</p><ul><li>甲</li><li>乙</li></ul>");

        Assert.Contains("## 标题", markdown);
        Assert.Contains("第一段**加粗**。\n\n第二段", markdown);
        Assert.Contains("[链接](https://x.com)", markdown);
        Assert.Contains("- 甲", markdown);
        Assert.Contains("- 乙", markdown);
    }

    /// <summary>
    /// 最坏形态:一行压缩且以普通文本开头。块级标签先断开,段落仍然分开,文本行不被毁。
    /// </summary>
    [Fact]
    public void DirectReader_CleanMarkup_SingleLineMixedHtml_SplitsBlocks()
    {
        string markdown = DirectPageReader.CleanMarkup("开头的文字<p>第一段</p><p>第二段</p>");

        Assert.Contains("开头的文字", markdown);
        Assert.Contains("第一段\n\n第二段", markdown);
    }

    /// <summary>纯文本没有标签,Clean 原样放行——不会把正文毁掉</summary>
    [Fact]
    public void DirectReader_CleanMarkup_PlainText_PassesThrough()
    {
        Assert.Equal("hello world", DirectPageReader.CleanMarkup("hello world"));
    }

    /// <summary>时间线、论坛这类页面一条帖子一个 article：没有 main 时要读到每一条，不能只给第一条</summary>
    [Fact]
    public async Task Extraction_ReadsEveryArticleOfATimeline()
    {
        string html = "<html><body><header><a href='/'>Site name</a></header><div id='feed'>" +
                      "<article><p>First post about kuroko.</p></article>" +
                      "<article><p>Second post about misaka.</p></article>" +
                      "<article><p>Third post about uiharu.</p><article><p>A reply.</p></article></article>" +
                      "</div></body></html>";

        string text = await DirectPageReader.ExtractTextAsync(html, CancellationToken.None);

        Assert.Contains("First post", text);
        Assert.Contains("Second post", text);
        Assert.Contains("Third post", text);
        Assert.DoesNotContain("Site name", text);
    }

    /// <summary>文章自己的 header 装着标题，留下；页眉、main 里的整页标题栏（维基的语言列表）是噪音</summary>
    [Fact]
    public async Task Extraction_KeepsOnlyTheArticlesOwnHeader()
    {
        string html = "<html><body><header>Site banner</header><main>" +
                      "<header><ul><li>English</li><li>Español</li></ul></header><article>" +
                      "<header><h1>The real title</h1></header><p>Body text of the article.</p>" +
                      "</article></main></body></html>";

        string text = await DirectPageReader.ExtractTextAsync(html, CancellationToken.None);

        Assert.Contains("The real title", text);
        Assert.Contains("Body text", text);
        Assert.DoesNotContain("Site banner", text);
        Assert.DoesNotContain("Español", text);
    }

    /// <summary>编码只在响应头声明、页内没有 meta 的 GBK 页：要按响应头解码，不能当 UTF-8 读成乱码</summary>
    [Fact]
    public async Task Markup_DecodesWithCharsetFromResponseHeader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); //造 GBK 字节用，不依赖哪个测试先跑
        string paragraph = string.Concat(Enumerable.Repeat("初春饰利是风纪委员第一七七支部的成员。", 10));
        byte[] gbk = Encoding.GetEncoding("gbk").GetBytes($"<html><body><p>{paragraph}</p></body></html>");

        PageReadResult result = await DirectPageReader.ReadMarkupAsync(
            new MemoryStream(gbk), "text/html; charset=gbk", CancellationToken.None);

        Assert.Contains("风纪委员", result.Content);
    }
}
