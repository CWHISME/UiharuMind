using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 模型源：响应样本取自 2026-10 实测（HF tree / 魔搭 repo/files / 魔搭 dolphin 搜索）。
/// 守的是：换源只换地址、校验值取对字段、令牌只在设置了时才带。
/// </summary>
public class ModelSourceTests
{
    private const string HfTree = """
        [{"type":"file","oid":"c4d8","size":3135,"path":".gitattributes"},
         {"type":"directory","oid":"aa","size":0,"path":"sub"},
         {"type":"file","oid":"e22e","size":1198182848,"lfs":{"oid":"f9c9f1d3","size":1198182848,"pointerSize":135},"path":"sub/Qwen3 0.6B-BF16.gguf"}]
        """;

    private const string MsFiles = """
        {"Code":200,"Success":true,"Data":{"Files":[
          {"Name":"Qwen3-0.6B-BF16.gguf","Path":"Qwen3-0.6B-BF16.gguf","Sha256":"f9c9f1d3","Size":1198182848,"Type":"blob"},
          {"Name":"docs","Path":"docs","Sha256":"","Size":0,"Type":"tree"}]}}
        """;

    private const string MsSearch = """
        {"Code":200,"Success":true,"Data":{"Model":{"Models":[{"Path":"unsloth","Name":"Qwen3.8-27B-GGUF","Downloads":383829}],"TotalCount":1}}}
        """;

    [Fact]
    public async Task HuggingFace_ListsFilesWithLfsSha_AndSkipsDirectories()
    {
        StubHandler handler = new(HfTree);
        HuggingFaceModelSource source = new(new HttpClient(handler), "https://hf-mirror.com/", null);

        IReadOnlyList<ModelRepoFile> files = await source.ListFilesAsync("unsloth/Qwen3-0.6B-GGUF",
            TestContext.Current.CancellationToken);

        Assert.Equal("https://hf-mirror.com/api/models/unsloth/Qwen3-0.6B-GGUF/tree/main?recursive=true",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal(2, files.Count);
        Assert.Null(files[0].Sha256); //非 LFS 小文件没有内容 sha256
        Assert.Equal(new ModelRepoFile("sub/Qwen3 0.6B-BF16.gguf", 1198182848, "f9c9f1d3"), files[1]);
    }

    [Fact]
    public void HuggingFace_DownloadUrl_KeepsSlashesEscapesNames_AndCarriesToken()
    {
        HuggingFaceModelSource source = new(new HttpClient(), "https://hf-mirror.com", "hf_x");

        DownloadRequest request = source.CreateDownload("o/r", new ModelRepoFile("sub/a b.gguf", 1, "abc"), "/tmp/a");

        Assert.Equal("https://hf-mirror.com/o/r/resolve/main/sub/a%20b.gguf", request.Url.AbsoluteUri);
        Assert.Equal("abc", request.ExpectedSha256);
        Assert.Equal(1, request.Segments);
        Assert.Equal("Bearer hf_x", request.Headers!["Authorization"]);
    }

    [Fact]
    public async Task HuggingFace_WithoutToken_SendsNoAuthorization()
    {
        StubHandler handler = new("[]");
        HuggingFaceModelSource source = new(new HttpClient(handler), "https://huggingface.co", " ");

        await source.SearchAsync("qwen", EModelSearchSort.Downloads, 5, null, TestContext.Current.CancellationToken);

        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.Contains("filter=gguf", handler.LastRequest.RequestUri!.Query);
    }

    [Fact]
    public async Task ModelScope_ListsBlobs_AndSearchJoinsOwnerAndName()
    {
        ModelScopeModelSource files = new(new HttpClient(new StubHandler(MsFiles)), null);
        ModelScopeModelSource search = new(new HttpClient(new StubHandler(MsSearch)), null);

        ModelRepoFile file = Assert.Single(await files.ListFilesAsync("unsloth/Qwen3-0.6B-GGUF",
            TestContext.Current.CancellationToken));
        ModelRepoSummary repo = Assert.Single((await search.SearchAsync("qwen", EModelSearchSort.Downloads, 5, null,
            TestContext.Current.CancellationToken)).Items);

        Assert.Equal(new ModelRepoFile("Qwen3-0.6B-BF16.gguf", 1198182848, "f9c9f1d3"), file);
        Assert.Equal(new ModelRepoSummary("unsloth/Qwen3.8-27B-GGUF", 383829), repo);
    }

    [Fact]
    public async Task HuggingFace_Search_TakesCursorFromLinkHeader_AndSendsItBackOnOwnEndpoint()
    {
        // 镜像把接口重定向回官方，Link 里是官方地址；续页仍要发到镜像
        StubHandler handler = new("""[{"id":"o/a","downloads":3}]""")
        {
            Link = "<https://huggingface.co/api/models?search=tts&limit=1&cursor=eyJh%3D%3D>; rel=\"next\""
        };
        HuggingFaceModelSource source = new(new HttpClient(handler), "https://hf-mirror.com", null);

        ModelSearchPage first = await source.SearchAsync("tts", EModelSearchSort.Downloads, 1, null, TestContext.Current.CancellationToken);
        Assert.Equal("eyJh%3D%3D", first.NextCursor);
        Assert.Equal(new ModelRepoSummary("o/a", 3), Assert.Single(first.Items));

        handler.Link = null;
        ModelSearchPage last = await source.SearchAsync("tts", EModelSearchSort.Downloads, 1, first.NextCursor, TestContext.Current.CancellationToken);
        string url = handler.LastRequest!.RequestUri!.OriginalString;
        Assert.StartsWith("https://hf-mirror.com/api/models?search=tts&filter=gguf&sort=downloads&limit=1&", url);
        Assert.EndsWith("&cursor=eyJh%3D%3D", url);
        Assert.Null(last.NextCursor);
    }

    [Theory]
    [InlineData(null, 1, 3, "2")] //整页且没到总数：还有下一页
    [InlineData("2", 2, 3, null)] //到总数了
    [InlineData(null, 1, null, "2")] //没给总数：整页就当还有
    public async Task ModelScope_Search_PagesByNumber_SortedByDownloadsOnServer(string? cursor, int page,
        int? total, string? expectedNext)
    {
        string totalField = total == null ? "" : $",\"TotalCount\":{total}";
        string json = "{\"Data\":{\"Model\":{\"Models\":[{\"Path\":\"o\",\"Name\":\"a\",\"Downloads\":1}," +
                      "{\"Path\":\"o\",\"Name\":\"b\",\"Downloads\":1}]" + totalField + "}}}";
        StubHandler handler = new(json);
        ModelScopeModelSource source = new(new HttpClient(handler), null);

        ModelSearchPage result = await source.SearchAsync("tts", EModelSearchSort.Downloads, 2, cursor, TestContext.Current.CancellationToken);

        string body = handler.LastBody!;
        Assert.Contains($"\"pageNumber\":{page}", body);
        Assert.Contains("\"sortBy\":\"DownloadsCount\"", body);
        Assert.Equal(expectedNext, result.NextCursor);
    }

    [Theory]
    [InlineData(EModelSearchSort.Downloads, "sort=downloads", "\"DownloadsCount\"")]
    [InlineData(EModelSearchSort.Trending, "sort=trendingScore", "\"Default\"")] //魔搭没有近期热门，用综合
    [InlineData(EModelSearchSort.Likes, "sort=likes", "\"StarsCount\"")]
    [InlineData(EModelSearchSort.Updated, "sort=lastModified", "\"GmtModified\"")]
    [InlineData(EModelSearchSort.Created, "sort=createdAt", "\"GmtCreated\"")]
    public async Task Search_MapsSortToEachSource(EModelSearchSort sort, string hfQuery, string msSortBy)
    {
        StubHandler hf = new("[]");
        StubHandler ms = new("{}");

        await new HuggingFaceModelSource(new HttpClient(hf), "https://huggingface.co", null)
            .SearchAsync("tts", sort, 5, null, TestContext.Current.CancellationToken);
        await new ModelScopeModelSource(new HttpClient(ms), null)
            .SearchAsync("tts", sort, 5, null, TestContext.Current.CancellationToken);

        Assert.Contains(hfQuery, hf.LastRequest!.RequestUri!.Query);
        Assert.Contains($"\"sortBy\":{msSortBy}", ms.LastBody);
    }

    [Fact]
    public async Task Search_ReadsLikesAndDates()
    {
        // HF 的 lastModified 不在默认字段里，要 expand 才回
        StubHandler hf = new("""
            [{"id":"o/a","downloads":3,"likes":7,"lastModified":"2026-10-08T04:31:02.000Z","createdAt":"2026-10-05T16:18:18.000Z"}]
            """);
        StubHandler ms = new("""
            {"Data":{"Model":{"Models":[{"Path":"o","Name":"a","Downloads":3,"Stars":7,"CreatedTime":1791426060,"LastUpdatedTime":0}]}}}
            """);

        ModelRepoSummary hfRepo = Assert.Single((await new HuggingFaceModelSource(new HttpClient(hf),
            "https://huggingface.co", null).SearchAsync("a", EModelSearchSort.Likes, 5, null,
            TestContext.Current.CancellationToken)).Items);
        ModelRepoSummary msRepo = Assert.Single((await new ModelScopeModelSource(new HttpClient(ms), null)
            .SearchAsync("a", EModelSearchSort.Likes, 5, null, TestContext.Current.CancellationToken)).Items);

        Assert.Contains("expand[]=lastModified", hf.LastRequest!.RequestUri!.OriginalString);
        Assert.Equal(new ModelRepoSummary("o/a", 3, 7, DateTimeOffset.Parse("2026-10-08T04:31:02Z"),
            DateTimeOffset.Parse("2026-10-05T16:18:18Z")), hfRepo);
        Assert.Equal(new ModelRepoSummary("o/a", 3, 7, null, DateTimeOffset.FromUnixTimeSeconds(1791426060)), msRepo);
    }

    [Theory]
    [InlineData(DownloadSourceSettingConfig.SourceHuggingFace, "", "https://huggingface.co/")]
    [InlineData(DownloadSourceSettingConfig.SourceHfMirror, "", "https://hf-mirror.com/")]
    [InlineData(DownloadSourceSettingConfig.SourceCustom, "https://my.mirror/", "https://my.mirror/")]
    [InlineData(DownloadSourceSettingConfig.SourceCustom, "not a url", "https://huggingface.co/")] //填错退回官方
    [InlineData(DownloadSourceSettingConfig.SourceModelScope, "", "https://modelscope.cn/")]
    public void Factory_PicksEndpointFromSettings(string mode, string custom, string expectedPrefix)
    {
        DownloadSourceSettingConfig config = new() { ModelSource = mode, CustomEndpoint = custom };

        IModelSource source = ModelSources.Create(config, new HttpClient());
        DownloadRequest request = source.CreateDownload("o/r", new ModelRepoFile("a.gguf", 1, null), "/tmp/a");

        Assert.StartsWith(expectedPrefix, request.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/o/r/releases/download/b1/a.zip", "https://gh.proxy/",
        "https://gh.proxy/https://github.com/o/r/releases/download/b1/a.zip")]
    [InlineData("https://github.com/o/r/releases/download/b1/a.zip", "", "https://github.com/o/r/releases/download/b1/a.zip")]
    [InlineData("https://huggingface.co/o/r/resolve/main/a.gguf", "https://gh.proxy",
        "https://huggingface.co/o/r/resolve/main/a.gguf")] //只代理 GitHub
    public void GitHubProxy_OnlyRewritesGitHub(string url, string prefix, string expected)
    {
        Assert.Equal(expected, ModelSources.ApplyGitHubProxy(url, prefix));
    }

    [Theory]
    [InlineData("llama-b11438-bin-macos-arm64", "OSX", Architecture.Arm64, true)]
    [InlineData("llama-b11438-bin-macos-x64", "OSX", Architecture.Arm64, false)]
    [InlineData("llama-b11438-bin-win-vulkan-x64", "WINDOWS", Architecture.X64, true)]
    [InlineData("llama-b11438-bin-win-cuda-12.4-x64", "WINDOWS", Architecture.X64, false)]
    [InlineData("llama-b11438-bin-win-cpu-x64", "WINDOWS", Architecture.X64, false)]
    [InlineData("llama-b11438-bin-ubuntu-vulkan-arm64", "LINUX", Architecture.Arm64, true)]
    [InlineData("llama-b11438-bin-ubuntu-x64", "LINUX", Architecture.X64, false)]
    public void RecommendedEngineVariant(string name, string platform, Architecture arch, bool expected)
    {
        Assert.Equal(expected, LLamaCppVariants.IsRecommended(name, OSPlatform.Create(platform), arch));
    }

    [Theory]
    [InlineData("llama-b1-bin-macos-arm64.tar.gz", "llama-b1-bin-macos-arm64")] //整个 .tar.gz，而不是只去 .gz
    [InlineData("llama-b1-bin-win-vulkan-x64.zip", "llama-b1-bin-win-vulkan-x64")]
    [InlineData("notes.txt", "notes")]
    public void EngineVersionName_DropsWholeArchiveExtension(string fileName, string expected)
    {
        Assert.Equal(expected, SimpleArchiveHelper.GetNameWithoutArchiveExtension(fileName));
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public string? Link { get; set; }

        public string? LastBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            HttpResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (Link != null) response.Headers.TryAddWithoutValidation("Link", Link);
            return Task.FromResult(response);
        }
    }
}
