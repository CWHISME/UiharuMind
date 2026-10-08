using System.Security.Cryptography;
using UiharuMind.Core.AI.Character.PromptActions;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI.ImageGeneration;

/// <summary>
/// 本地生图模型：内置列表、换成引擎的启动描述与请求、组件下载与按引用删除、失败归类
/// </summary>
public class LocalImageModelTests : IDisposable
{
    private static readonly HashSet<string> KnownFlags =
        ["diffusion-model", "model", "vae", "llm", "llm_vision", "clip_l", "clip_g", "t5xxl"];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-local-image-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(32 * 1024);
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));

    public LocalImageModelTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        Thread.Sleep(100);
        Directory.Delete(_root, true);
    }

    [Fact]
    public void BuiltInCatalog_IsWellFormed()
    {
        ImageModelCatalog catalog = ImageModelCatalog.BuiltIn;

        Assert.NotEmpty(catalog.Families);
        Assert.Contains(catalog.Families, x => x.Recommended);
        foreach (ImageCatalogFamily family in catalog.Families)
        {
            Assert.True(family.Tiers.Count(x => x.Recommended) <= 1, family.Id);
            Assert.Contains(family.DefaultTier, family.Tiers);
            Assert.False(string.IsNullOrEmpty(family.DescriptionFor("zh-hans")), family.Id);
            foreach (ImageCatalogComponent component in family.Tiers.SelectMany(x => x.Components))
            {
                Assert.Contains(component.Flag, KnownFlags);
                Assert.True(component.Size > 0, component.File);
                Assert.Matches("^[0-9a-f]{64}$", component.Sha256 ?? "");
            }
        }
    }

    [Fact]
    public void LocalEntry_ResolvesComponentsUnderTheModelFolder_AndCarriesRecommendedSampling()
    {
        ImageCatalogFamily family = Family("f", ("diffusion-model", "owner/diffusion", "q4/model.gguf"), ("vae", "owner/vae", "split/ae.safetensors"));

        ImageModelInfo model = LocalImageModels.Create(family, family.DefaultTier, "F");
        SdCppModelSetup setup = LocalImageModels.ToSetup(model, _root);

        Assert.True(model.IsLocal);
        Assert.True(model.IsConfigured);
        Assert.Equal(8, model.Local!.Steps);
        Assert.Equal(Path.Combine(_root, "owner", "diffusion", "model.gguf"), setup.Components[0].Path);
        Assert.Equal(Path.Combine(_root, "owner", "vae", "ae.safetensors"), setup.Components[1].Path);
        Assert.Equal(2, LocalImageModels.MissingFiles(model, _root).Count);
    }

    [Fact]
    public void Request_KeepsTheBaseArea_AndAnEditFollowsItsMainImage()
    {
        LocalImageModel local = new() { BaseResolution = 1024, Steps = 4, CfgScale = 1 };

        SdCppImageRequest wide = LocalImageModels.ToRequest(local, new ImageRequest("p", [], new ImageAspectRatio(16, 9)));
        SdCppImageRequest edit = LocalImageModels.ToRequest(local,
            new ImageRequest("p", [new ImageInput(PngHeader(600, 900), "image/png")], null));

        Assert.Equal((1344, 768), (wide.Width, wide.Height));
        Assert.Equal((832, 1280), (edit.Width, edit.Height)); //2:3
        Assert.Single(edit.ReferenceImages);
    }

    [Fact]
    public async Task Store_DownloadsOnlyMissing_MarksComponents_AndDeleteKeepsSharedAndUserModels()
    {
        using TestHttpServer server = new(_payload);
        // 用户之前当对话模型下过的 LLM：清单里没标只作组件
        string userRepo = Path.Combine(_root, "user", "llm");
        Directory.CreateDirectory(userRepo);
        await File.WriteAllBytesAsync(Path.Combine(userRepo, "llm.gguf"), _payload, TestContext.Current.CancellationToken);
        new ModelManifest
        {
            Source = "fake", Repository = "user/llm",
            Models = [new ManifestModel { Files = [new ManifestFile { Path = "llm.gguf" }] }]
        }.Save(userRepo);

        ImageCatalogFamily family = Family("a", ("diffusion-model", "own/diffusion", "a.gguf"),
            ("vae", "shared/vae", "ae.safetensors"), ("llm", "user/llm", "llm.gguf"));
        ImageModelInfo first = LocalImageModels.Create(family, family.DefaultTier, "A");
        ImageModelInfo second = new()
        {
            Name = "B", Dialect = EImageDialect.Local,
            Local = new LocalImageModel { Components = [new LocalImageComponent { Flag = "vae", Repository = "shared/vae", File = "ae.safetensors" }] }
        };
        List<ImageModelInfo> models = [first, second];
        LocalImageModelStore store = new(new ModelRepoDownloader(_queue), () => _root, () => new FakeSource(server.Url), () => models);
        int installed = 0;

        IReadOnlyList<DownloadJob> jobs = store.Enqueue(family.DefaultTier, () =>
        {
            Interlocked.Increment(ref installed);
            return Task.CompletedTask;
        });

        Assert.Equal(2, jobs.Count); //用户已有的 LLM 不再下
        await WaitFor(() => installed == 1);
        Assert.True(store.IsInstalled(family.DefaultTier));
        Assert.True(ModelManifest.TryLoad(Path.Combine(_root, "own", "diffusion"))!.Models.Single().ComponentOnly);

        IReadOnlyList<string> deleted = store.DeleteComponents(first);

        Assert.Equal([Path.Combine(_root, "own", "diffusion", "a.gguf")], deleted);
        Assert.False(Directory.Exists(Path.Combine(_root, "own", "diffusion")));
        Assert.True(File.Exists(Path.Combine(_root, "shared", "vae", "ae.safetensors"))); //B 还在用
        Assert.True(File.Exists(Path.Combine(userRepo, "llm.gguf"))); //用户的对话模型
    }

    [Fact]
    public async Task Dialect_MissingFilesOrNoEngine_IsUnavailable_SoTheChainMovesOn()
    {
        ImageCatalogFamily family = Family("f", ("diffusion-model", "owner/d", "d.gguf"));
        ImageModelInfo model = LocalImageModels.Create(family, family.DefaultTier, "F");
        using SdCppEngine engine = new(new SdCppSettingConfig(), Path.Combine(_root, "engines"), _queue);
        LocalImageDialect dialect = new(() => engine, () => _root);
        ImageRequest request = new("cat", [], null);
        CancellationToken token = TestContext.Current.CancellationToken;

        ImageOutcome missing = await dialect.GenerateAsync(model, request, token);
        Assert.Equal(EImageFailureKind.Unavailable, missing.Failure!.Kind);
        Assert.Contains("missing files", missing.Failure.Message);

        Directory.CreateDirectory(Path.Combine(_root, "owner", "d"));
        await File.WriteAllTextAsync(Path.Combine(_root, "owner", "d", "d.gguf"), "", token);
        ImageOutcome noEngine = await dialect.GenerateAsync(model, request, token);
        Assert.Equal(EImageFailureKind.Unavailable, noEngine.Failure!.Kind);
        Assert.Contains("not installed", noEngine.Failure.Message);
    }

    private static ImageCatalogFamily Family(string id, params (string Flag, string Repo, string File)[] components) =>
        new(id, id, new Dictionary<string, string> { ["en"] = id }, "", false, false, true, 1024,
            new ImageCatalogSampling(8, 1f),
            [new ImageCatalogTier("balanced", components.Select(x => new ImageCatalogComponent(x.Flag, x.Repo, x.File, 1, null)).ToList()) { Recommended = true }]);

    // TryReadSize 只看文件头：签名 + IHDR 的宽高
    private static byte[] PngHeader(int width, int height)
    {
        byte[] bytes = new byte[33];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R' }
            .CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "condition not met within 10s");
    }

    private sealed class FakeSource(Uri url) : IModelSource
    {
        public string Id => "fake";

        public Task<ModelSearchPage> SearchAsync(string query, EModelSearchSort sort, int limit, string? cursor,
            CancellationToken token) => throw new NotSupportedException();

        public Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken token,
            string? revision = null) => throw new NotSupportedException();

        public Task<string?> GetReadmeAsync(string repository, CancellationToken token) => Task.FromResult<string?>(null);

        public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath,
            string? revision = null) => new(url, destinationPath, 1, null);
    }
}
