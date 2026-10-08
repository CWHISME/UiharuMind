using System.Security.Cryptography;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;
using UiharuMind.Core.Tests.Utils;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// audio.cpp 模型包：spec 解析、落盘布局（target_directory + strip_prefix）、包标记、按标记删除、下载源与镜像
/// </summary>
public class AudioCppPackageTests : IDisposable
{
    private const string PocketSpec = """
        {
          "family": "pocket_tts", "category": "tts", "status": "supported", "languages": ["en", "de"],
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf", "revision": "main" } },
          "packages": [
            { "id": "pocket_tts_english_q8_0", "display_name": "PocketTTS English Q8_0 GGUF", "default": true,
              "format": "gguf", "precision": "q8_0", "target_directory": "PocketTTS-GGUF/english",
              "files": ["PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf", "PocketTTS-GGUF/english/embeddings/alba.safetensors"],
              "strip_prefix": "PocketTTS-GGUF/english" },
            { "id": "pocket_tts_english_st", "display_name": "safetensors", "format": "safetensors", "precision": "f32",
              "target_directory": "x", "files": ["x/a.safetensors"] },
            { "id": "pocket_tts_pinned", "display_name": "pinned", "format": "gguf", "precision": "q8_0",
              "target_directory": "Pinned", "files": ["p.gguf"],
              "download": { "repo": "someone/other", "revision": "abc123" } },
            { "id": "pocket_tts_gone", "display_name": "gone", "format": "gguf", "precision": "q8_0",
              "target_directory": "Gone", "files": ["g.gguf"], "download": { "kind": "unsupported" } }
          ]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-audiopkg-{Guid.NewGuid():N}");
    private readonly byte[] _payload = RandomNumberGenerator.GetBytes(32 * 1024);
    private readonly DownloadQueue _queue = new(new FileDownloader(new HttpClient()));

    public void Dispose()
    {
        foreach (DownloadJob job in _queue.Jobs) _queue.Cancel(job);
        Thread.Sleep(100);
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    [Fact]
    public void Parse_KeepsGgufPackages_WithMergedDownload()
    {
        IReadOnlyList<AudioCppPackage> packages = AudioCppModelCatalog.Parse(PocketSpec);

        Assert.Equal(["pocket_tts_english_q8_0", "pocket_tts_pinned"], packages.Select(x => x.Id));
        AudioCppPackage pocket = packages[0];
        Assert.Equal(EAudioCppTask.Tts, pocket.Task);
        Assert.Equal("audio-cpp/audio.cpp-gguf", pocket.Repository);
        Assert.Null(pocket.Revision); // main 交给模型源的默认分支
        Assert.True(pocket.IsDefault);
        Assert.Equal("pocket-tts-english-q8_0", pocket.ModelName);
        Assert.False(pocket.NeedsEspeak);

        AudioCppPackage pinned = packages[1];
        Assert.Equal("someone/other", pinned.Repository);
        Assert.Equal("abc123", pinned.Revision);
    }

    [Fact]
    public void Parse_IgnoresNonSpeechFamilies_AndFlagsEspeak()
    {
        Assert.Empty(AudioCppModelCatalog.Parse("""{ "family": "silero_vad", "category": "vad", "packages": [] }"""));

        AudioCppPackage kokoro = Assert.Single(AudioCppModelCatalog.Parse("""
            { "family": "kokoro_tts", "category": "tts", "runtime": { "phonemizer": "espeak-ng" },
              "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "r/r" } },
              "packages": [ { "id": "k", "format": "gguf", "target_directory": "K", "files": ["K/k.gguf"] } ] }
            """));
        Assert.True(kokoro.NeedsEspeak);
    }

    [Fact]
    public void LocalPath_StripsPrefix_AndRejectsEscapes()
    {
        AudioCppPackage pocket = AudioCppModelCatalog.Parse(PocketSpec)[0];

        Assert.Equal(Path.Combine("embeddings", "alba.safetensors"),
            pocket.LocalPath("PocketTTS-GGUF/english/embeddings/alba.safetensors"));
        Assert.Null(pocket.LocalPath("Other/x.gguf"));
        Assert.Null(pocket.LocalPath("PocketTTS-GGUF/english/../../evil.gguf"));
    }

    [Fact]
    public async Task Enqueue_LandsInTargetDirectory_WritesMarker_ThenDeletesCleanly()
    {
        using TestHttpServer server = new(_payload);
        AudioCppPackage pocket = AudioCppModelCatalog.Parse(PocketSpec)[0];
        AudioCppPackageStore store = CreateStore(server.Url);
        int installed = 0;

        IReadOnlyList<DownloadJob> jobs = await store.EnqueueAsync(pocket, () =>
        {
            Interlocked.Increment(ref installed);
            return Task.CompletedTask;
        }, TestContext.Current.CancellationToken);

        Assert.Equal(2, jobs.Count);
        await WaitFor(() => installed > 0);
        string directory = Path.Combine(_root, "audio.cpp", "PocketTTS-GGUF", "english");
        Assert.Equal(directory, store.PackageDirectory(pocket));
        Assert.True(File.Exists(Path.Combine(directory, "pocket-tts-english-q8_0.gguf")));
        Assert.True(File.Exists(Path.Combine(directory, "embeddings", "alba.safetensors")));
        Assert.True(File.Exists(Path.Combine(directory, ".audiocpp-package-pocket_tts_english_q8_0.json")));
        Assert.True(store.IsInstalled(pocket));
        Assert.Equal(1, installed);

        // 从扫描到的 gguf 删：连伴生文件、标记与空目录一起收掉，audio.cpp 根留着
        IReadOnlyList<string> deleted = store.DeleteContaining(Path.Combine(directory, "pocket-tts-english-q8_0.gguf"));
        Assert.Equal(3, deleted.Count);
        Assert.False(Directory.Exists(Path.Combine(_root, "audio.cpp", "PocketTTS-GGUF")));
        Assert.True(Directory.Exists(Path.Combine(_root, "audio.cpp")));
        Assert.False(store.IsInstalled(pocket));
    }

    [Fact]
    public async Task Enqueue_AllFilesPresent_OnlyWritesMarker()
    {
        AudioCppPackage pocket = AudioCppModelCatalog.Parse(PocketSpec)[0];
        AudioCppPackageStore store = CreateStore(new Uri("http://127.0.0.1:1/"));
        string directory = store.PackageDirectory(pocket);
        Directory.CreateDirectory(Path.Combine(directory, "embeddings"));
        await File.WriteAllBytesAsync(Path.Combine(directory, "pocket-tts-english-q8_0.gguf"), _payload, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "embeddings", "alba.safetensors"), _payload, TestContext.Current.CancellationToken);

        Assert.Empty(await store.EnqueueAsync(pocket, cancellationToken: TestContext.Current.CancellationToken));
        Assert.True(store.IsInstalled(pocket));
    }

    [Fact]
    public async Task Enqueue_FileMissingFromRepository_Fails()
    {
        AudioCppPackage pocket = AudioCppModelCatalog.Parse(PocketSpec)[0];
        AudioCppPackageStore store = new(_queue, () => _root,
            _ => new AudioCppPackageSource(new FakeSource(new Uri("http://127.0.0.1:1/"), _payload.Length, "embeddings"),
                "audio-cpp/audio.cpp-gguf", null));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnqueueAsync(pocket, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Source_OnModelScope_UsesMirrorForOfficialRepo_AndHfMirrorOtherwise()
    {
        IReadOnlyList<AudioCppPackage> packages = AudioCppModelCatalog.Parse(PocketSpec);
        DownloadSourceSettingConfig modelScope = new() { ModelSource = DownloadSourceSettingConfig.SourceModelScope };

        AudioCppPackageSource official = AudioCppPackageSource.Resolve(packages[0], modelScope);
        Assert.Equal("modelscope", official.Source.Id);
        Assert.Equal("HereIsMark/audio.cpp-gguf", official.Repository);

        AudioCppPackageSource other = AudioCppPackageSource.Resolve(packages[1], modelScope);
        Assert.Equal("huggingface", other.Source.Id);
        Assert.Equal("someone/other", other.Repository);
        Assert.Equal("abc123", other.Revision);
        Assert.StartsWith(DownloadSourceSettingConfig.HfMirrorEndpoint,
            other.Source.CreateDownload(other.Repository, new ModelRepoFile("p.gguf", 1, null), "/tmp/p", other.Revision).Url.AbsoluteUri);

        AudioCppPackageSource huggingFace = AudioCppPackageSource.Resolve(packages[1], new DownloadSourceSettingConfig());
        Assert.Contains("/resolve/abc123/", huggingFace.Source
            .CreateDownload(huggingFace.Repository, new ModelRepoFile("p.gguf", 1, null), "/tmp/p", huggingFace.Revision).Url.AbsoluteUri);
    }

    private AudioCppPackageStore CreateStore(Uri url) => new(_queue, () => _root,
        _ => new AudioCppPackageSource(new FakeSource(url, _payload.Length), "audio-cpp/audio.cpp-gguf", null));

    private static async Task WaitFor(Func<bool> condition)
    {
        for (int i = 0; i < 500 && !condition(); i++)
            await Task.Delay(20);
        Assert.True(condition(), "condition not met within 10s");
    }

    // 仓库里列出 pocket 两个文件（可去掉某个）；下载都指向同一个测试服务
    private sealed class FakeSource(Uri url, long size, string? missing = null) : IModelSource
    {
        public string Id => "fake";

        public Task<ModelSearchPage> SearchAsync(string query, EModelSearchSort sort, int limit, string? cursor,
            CancellationToken token) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ModelRepoFile>> ListFilesAsync(string repository, CancellationToken token,
            string? revision = null)
        {
            IReadOnlyList<ModelRepoFile> files = new[]
                {
                    "PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf",
                    "PocketTTS-GGUF/english/embeddings/alba.safetensors",
                    "Other/unrelated.gguf"
                }
                .Where(x => missing == null || !x.Contains(missing))
                .Select(x => new ModelRepoFile(x, size, null))
                .ToList();
            return Task.FromResult(files);
        }

        public Task<string?> GetReadmeAsync(string repository, CancellationToken token) => Task.FromResult<string?>(null);

        public DownloadRequest CreateDownload(string repository, ModelRepoFile file, string destinationPath,
            string? revision = null) =>
            new(url, destinationPath, 1, file.Sha256);
    }
}
