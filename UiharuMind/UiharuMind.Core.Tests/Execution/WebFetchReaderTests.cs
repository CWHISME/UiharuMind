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
        Assert.NotNull(FirecrawlPageReader.Parse("""{"success":false}""").Error);
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
