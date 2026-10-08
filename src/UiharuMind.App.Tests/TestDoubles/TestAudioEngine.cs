using System.Text.Json.Nodes;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.Core.DownloadHelper;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 临时目录里的 audio.cpp：可选装好引擎（带内联 spec），模型源不联网，可以假装装好某个包。
/// 三个家族：PocketTTS（推荐、合成、预置音色 alba）、Kokoro（社区、要 eSpeak）、Qwen3-ASR（识别）
/// </summary>
internal sealed class TestAudioEngine : IDisposable
{
    private const string PocketSpec = """
        {
          "family": "pocket_tts", "display_name": "PocketTTS", "category": "tts", "status": "supported",
          "description": "Kyutai 100M-parameter CPU-friendly TTS.",
          "tasks": ["tts", "clone"], "modes": ["offline", "streaming"], "languages": ["en", "de"],
          "capabilities": { "clone": ["speaker_reference"], "tts": ["long_form"] },
          "ui": { "recommended_package": "pocket_tts_english_q8_0", "builtin_voices": ["alba"] },
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf" } },
          "packages": [
            { "id": "pocket_tts_english_q8_0", "display_name": "PocketTTS English Q8_0 GGUF", "format": "gguf",
              "precision": "q8_0", "target_directory": "PocketTTS-GGUF/english", "strip_prefix": "PocketTTS-GGUF/english",
              "files": ["PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf"] },
            { "id": "pocket_tts_german_q8_0", "display_name": "PocketTTS German Q8_0 GGUF", "format": "gguf",
              "precision": "q8_0", "target_directory": "PocketTTS-GGUF/german", "strip_prefix": "PocketTTS-GGUF/german",
              "files": ["PocketTTS-GGUF/german/pocket-tts-german-q8_0.gguf"] }
          ]
        }
        """;

    private const string KokoroSpec = """
        {
          "family": "kokoro_tts", "display_name": "Kokoro", "category": "tts", "status": "community",
          "description": "Kokoro 82M.", "tasks": ["tts"], "modes": ["offline"], "languages": ["en-us", "zh"],
          "runtime": { "phonemizer": "espeak-ng" }, "ui": {},
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf" } },
          "packages": [
            { "id": "kokoro_82m_q8_0", "display_name": "Kokoro 82M Q8_0 GGUF", "format": "gguf", "precision": "q8_0",
              "target_directory": "Kokoro-82M-GGUF", "files": ["Kokoro-82M-GGUF/kokoro-82m-q8_0.gguf"] }
          ]
        }
        """;

    private const string QwenAsrSpec = """
        {
          "family": "qwen3_asr", "display_name": "Qwen3-ASR", "category": "asr", "status": "supported",
          "description": "Qwen ASR.", "tasks": ["asr"], "modes": ["offline", "streaming"], "languages": ["zh", "en"],
          "capabilities": { "asr": ["word_timestamps", "partial_results"] }, "ui": {},
          "package_defaults": { "download": { "kind": "huggingface_snapshot", "repo": "audio-cpp/audio.cpp-gguf" } },
          "packages": [
            { "id": "qwen3_asr_0_6b_q8_0", "display_name": "Qwen3-ASR 0.6B Q8_0 GGUF", "format": "gguf", "precision": "q8_0",
              "target_directory": "Qwen3-ASR-0.6B-GGUF", "files": ["Qwen3-ASR-0.6B-GGUF/qwen3-asr-0.6b-q8_0.gguf"] }
          ]
        }
        """;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"uiharu-audio-{Guid.NewGuid():N}");

    /// <param name="installEngine">装好引擎（否则引擎目录是空的）</param>
    /// <param name="scanModels">扫描到的本地模型；默认没有</param>
    public TestAudioEngine(bool installEngine = true, Func<IReadOnlyList<AudioCppModelEntry>>? scanModels = null)
    {
        string engines = Path.Combine(_root, "engines");
        Directory.CreateDirectory(engines);
        if (installEngine)
        {
            string version = Path.Combine(engines, "audio-v0.9.1-bin-macos-arm64-metal");
            Directory.CreateDirectory(Path.Combine(version, "model_specs"));
            File.WriteAllText(Path.Combine(version, AudioCppVersionManager.ServerExeName), "");
            File.WriteAllText(Path.Combine(version, "model_specs", "pocket_tts.json"), PocketSpec);
            File.WriteAllText(Path.Combine(version, "model_specs", "kokoro_tts.json"), KokoroSpec);
            File.WriteAllText(Path.Combine(version, "model_specs", "qwen3_asr.json"), QwenAsrSpec);
        }

        Engine = new AudioCppEngine(Settings, engines, Path.Combine(_root, "server.json"), scanModels ?? (() => []));
        Source = new FakeModelSource
        {
            Files =
            [
                new ModelRepoFile("PocketTTS-GGUF/english/pocket-tts-english-q8_0.gguf", 134_000_000, null),
                new ModelRepoFile("PocketTTS-GGUF/german/pocket-tts-german-q8_0.gguf", 128_000_000, null),
                new ModelRepoFile("Kokoro-82M-GGUF/kokoro-82m-q8_0.gguf", 89_000_000, null),
                new ModelRepoFile("Qwen3-ASR-0.6B-GGUF/qwen3-asr-0.6b-q8_0.gguf", 1_100_000_000, null)
            ]
        };
        Store = new AudioCppPackageStore(Queue, () => ModelRoot,
            package => new AudioCppPackageSource(Source, package.Repository, null));
    }

    /// <summary>引擎设置（不落盘也无妨：测试只读写内存里的这份）</summary>
    public AudioCppSettingConfig Settings { get; } = new();

    public AudioCppEngine Engine { get; }

    public AudioCppPackageStore Store { get; }

    public FakeModelSource Source { get; }

    /// <summary>独立的下载队列，不碰全局</summary>
    public DownloadQueue Queue { get; } = new(new FileDownloader(new HttpClient()));

    public string ModelRoot => Path.Combine(_root, "models");

    /// <summary>
    /// 假装装好一个包：写出它的文件与包标记，返回 gguf 路径
    /// </summary>
    /// <param name="directory">包目录（相对 audio.cpp 根）</param>
    /// <param name="packageId">包 id</param>
    /// <param name="fileName">gguf 文件名</param>
    /// <returns>gguf 的完整路径</returns>
    public string Install(string directory, string packageId, string fileName)
    {
        string packageDirectory = Path.Combine(ModelRoot, AudioCppPackageStore.RootName, directory);
        Directory.CreateDirectory(packageDirectory);
        string file = Path.Combine(packageDirectory, fileName);
        File.WriteAllBytes(file, new byte[2048]);
        JsonObject marker = new() { ["package_id"] = packageId, ["uiharu_local_files"] = new JsonArray(fileName) };
        File.WriteAllText(Path.Combine(packageDirectory, $".audiocpp-package-{packageId}.json"), marker.ToJsonString());
        return file;
    }

    public void Dispose()
    {
        foreach (DownloadJob job in Queue.Jobs) Queue.Cancel(job);
        Engine.Dispose();
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
