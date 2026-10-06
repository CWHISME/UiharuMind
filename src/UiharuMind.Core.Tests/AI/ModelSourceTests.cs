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

        await source.SearchAsync("qwen", 5, TestContext.Current.CancellationToken);

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
        ModelRepoSummary repo = Assert.Single(await search.SearchAsync("qwen", 5,
            TestContext.Current.CancellationToken));

        Assert.Equal(new ModelRepoFile("Qwen3-0.6B-BF16.gguf", 1198182848, "f9c9f1d3"), file);
        Assert.Equal(new ModelRepoSummary("unsloth/Qwen3.8-27B-GGUF", 383829), repo);
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
