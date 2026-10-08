using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 改 <see cref="ModelSettingConfig.Current.LocalModelPath"/> 的测试必须串行：
/// 全局只有一个模型目录配置，并行跑会在彼此的临时目录上撞车
/// </summary>
[CollectionDefinition("LocalModelSettingState")]
public sealed class LocalModelSettingStateCollectionDefinition;

/// <summary>
/// audio.cpp 自家打包的 GGUF（ADR 0071）：容器是标准 GGUF v3，但键全在 audiocpp.* 命名空间，
/// 没有 llama.cpp 那套 context_length / file_type。判定与读取都必须走 audiocpp 分支。
/// </summary>
[Collection("LocalModelSettingState")]
public class AudioCppGgufTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-audiocpp-{Guid.NewGuid():N}");
    private readonly string _originalPath = ModelSettingConfig.Current.LocalModelPath;

    public AudioCppGgufTests()
    {
        Directory.CreateDirectory(_directory);
        ModelSettingConfig.Current.LocalModelPath = _directory;
    }

    public void Dispose()
    {
        ModelSettingConfig.Current.LocalModelPath = _originalPath;
        Directory.Delete(_directory, true);
    }

    /// <summary>
    /// 造一个 audio.cpp 形状的 GGUF；category 传空串表示老包（spec 里没有 category 字段）
    /// </summary>
    private static string WriteAudioGguf(string directory, string fileName, string family, string category, string displayName = "")
    {
        string spec = $"{{\"family\":\"{family}\"";
        if (category.Length > 0) spec += $",\"category\":\"{category}\"";
        if (displayName.Length > 0) spec += $",\"display_name\":\"{displayName}\"";
        spec += ",\"packages\":[{\"default\":true,\"precision\":\"q8_0\"}]}";

        string path = Path.Combine(directory, fileName);
        new GGufBuilder()
            .String("general.architecture", "audiocpp")
            .String("general.name", Path.GetFileNameWithoutExtension(fileName))
            .String("audiocpp.model_spec.family", family)
            .UInt32("audiocpp.model_spec.version", 1)
            .String("audiocpp.model_spec.json", spec)
            .String("audiocpp.weight_type", "q8_0")
            .Tensor("w", 8)
            .WriteTo(path);
        return path;
    }

    [Fact]
    public void Classifies_TtsAndAsr_BySpecCategory()
    {
        GGufHeader tts = GGufHeaderReader.Read(WriteAudioGguf(_directory, "tts-x.gguf", "pocket_tts", "tts", "PocketTTS English Q8_0 GGUF"));
        GGufHeader asr = GGufHeaderReader.Read(WriteAudioGguf(_directory, "asr-y.gguf", "moonshine_asr", "asr"));

        Assert.Equal(ELocalModelKind.SpeechSynthesis, LocalModelKindClassifier.Classify(tts));
        Assert.Equal(ELocalModelKind.SpeechRecognition, LocalModelKindClassifier.Classify(asr));
    }

    [Fact]
    public void LegacySpec_WithoutCategory_InfersFromFamilySuffix()
    {
        // 老包内嵌 spec 只有 family（pocket_tts 实测如此），category 由 _tts 后缀推断
        GGufHeader legacy = GGufHeaderReader.Read(WriteAudioGguf(_directory, "legacy.gguf", "pocket_tts", ""));

        Assert.Equal(ELocalModelKind.SpeechSynthesis, LocalModelKindClassifier.Classify(legacy));
    }

    [Fact]
    public void LegacySpec_WithoutFamily_FallsBackToFamilyKey()
    {
        // kokoro 的老包：内嵌 spec 只有 sources 等字段，family 只在单独的键里
        string path = Path.Combine(_directory, "kokoro.gguf");
        new GGufBuilder()
            .String("general.architecture", "audiocpp")
            .String("audiocpp.model_spec.family", "kokoro_tts")
            .String("audiocpp.model_spec.json", "{\"sources\":[]}")
            .Tensor("w", 8)
            .WriteTo(path);

        Assert.Equal(ELocalModelKind.SpeechSynthesis, LocalModelKindClassifier.Classify(GGufHeaderReader.Read(path)));
    }

    [Fact]
    public void HugeEmbeddedByteArray_StillReadable()
    {
        // kokoro 把 38MB 的伴生文件塞进元数据里的字节数组，超过字符串数组的个数上限也得能跳过
        string path = Path.Combine(_directory, "embedded.gguf");
        new GGufBuilder()
            .String("general.architecture", "audiocpp")
            .String("audiocpp.model_spec.family", "kokoro_tts")
            .UInt8Array("audiocpp.embedded_files.data", new byte[(1 << 24) + 1])
            .Tensor("w", 8)
            .WriteTo(path);

        Assert.Equal(ELocalModelKind.SpeechSynthesis, GGufMetadataReader.TryRead(path)?.Kind);
    }

    [Fact]
    public void UnknownAudioCategory_IsNeverChat()
    {
        // VAD、音源分离这类 audio.cpp 模型 llama 引擎读不了，归到对话就会在加载时失败
        GGufHeader vad = GGufHeaderReader.Read(WriteAudioGguf(_directory, "vad.gguf", "silero_vad", "vad"));

        Assert.Equal(ELocalModelKind.OtherAudio, LocalModelKindClassifier.Classify(vad));
    }

    [Fact]
    public void PersistedKindValues_DoNotShift()
    {
        // 种类按整数缓存在模型配置里，挪动已有成员会把老缓存读成别的种类
        Assert.Equal(0, (int)ELocalModelKind.Chat);
        Assert.Equal(1, (int)ELocalModelKind.Embedding);
        Assert.Equal(2, (int)ELocalModelKind.Reranker);
        Assert.Equal(3, (int)ELocalModelKind.Projector);
    }

    [Fact]
    public void TryRead_ReadsAudioCppFields()
    {
        GGufMetadataInfo? info = GGufMetadataReader.TryRead(WriteAudioGguf(_directory, "tts-a.gguf", "pocket_tts", "tts"));

        Assert.NotNull(info);
        Assert.Equal("audiocpp", info.Architecture);
        Assert.Equal("q8_0", info.Quantization); // audiocpp.weight_type
        Assert.Equal(0, info.ContextLength); // 语音模型没有上下文长度的概念
        Assert.Equal(ELocalModelKind.SpeechSynthesis, info.Kind);
        Assert.Equal(8ul, info.ParameterCount);
    }

    [Fact]
    public void NeedsMetadata_StableForSpeechKinds_AfterOneRead()
    {
        GGufModelInfo model = new() { ModelName = "x" };
        model.ApplyMetadata(GGufMetadataReader.TryRead(WriteAudioGguf(_directory, "tts-b.gguf", "pocket_tts", "tts")));

        Assert.True(model.IsReady);
        Assert.False(model.NeedsMetadata); // 种类已判出，ContextLength 为 0 也不重读
        Assert.Equal(ELocalModelKind.SpeechSynthesis, model.Kind);
    }

    [Fact]
    public void Scanner_KeepsSpeechModelsOutOfChatList()
    {
        WriteAudioGguf(_directory, "tts-c.gguf", "pocket_tts", "tts");
        WriteAudioGguf(_directory, "asr-d.gguf", "moonshine_asr", "asr");

        IReadOnlyList<LocalModelEntry> all = LocalModelScanner.Scan(force: true);
        Assert.Equal(2, all.Count);
        Assert.DoesNotContain(all, x => x.Info.Kind == ELocalModelKind.Chat);

        string[] chat = LocalModelScanner.Scan(ELocalModelKind.Chat, force: true)
            .Select(x => x.Info.ModelName).Order().ToArray();
        Assert.Empty(chat);

        Assert.Equal(["tts-c"], LocalModelScanner.Scan(ELocalModelKind.SpeechSynthesis, force: true)
            .Select(x => x.Info.ModelName).ToArray());
        Assert.Equal(["asr-d"], LocalModelScanner.Scan(ELocalModelKind.SpeechRecognition, force: true)
            .Select(x => x.Info.ModelName).ToArray());
    }

    [Fact]
    public void Scanner_SecondPass_DoesNotRereadSpeechFiles()
    {
        WriteAudioGguf(_directory, "tts-e.gguf", "pocket_tts", "tts");

        LocalModelScanner.Scan(force: true);
        LocalModelScanner.Scan(force: false);

        // 第二遍不重读：种类已缓存且 NeedsMetadata 为 false；重读会走 TryRead，这里只能间接验证不抛、结果一致
        GGufModelInfo info = Assert.Single(LocalModelScanner.Scan(ELocalModelKind.SpeechSynthesis)).Info;
        Assert.False(info.NeedsMetadata);
        Assert.Equal(ELocalModelKind.SpeechSynthesis, info.Kind);
    }
}
