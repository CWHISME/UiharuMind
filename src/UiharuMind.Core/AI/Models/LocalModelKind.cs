namespace UiharuMind.Core.AI.Models;

/// <summary>
/// 本地 GGUF 的种类，由文件头判定（见 ADR 0067）
/// </summary>
public enum ELocalModelKind
{
    Chat,
    Embedding,
    Reranker,

    /// <summary>
    /// 视觉投影（mmproj），是视觉模型的附件，不单独作为模型
    /// </summary>
    Projector,

    // 以下为 audio.cpp 的 GGUF（ADR 0071）。种类按整数缓存在模型配置里，新成员只能往后加

    /// <summary>
    /// 语音合成（TTS）
    /// </summary>
    SpeechSynthesis,

    /// <summary>
    /// 语音识别（ASR）
    /// </summary>
    SpeechRecognition,

    /// <summary>
    /// audio.cpp 的其他任务（VAD、音源分离、音乐生成等），暂不进任何列表
    /// </summary>
    OtherAudio
}

/// <summary>
/// 种类的归属判断
/// </summary>
public static class LocalModelKindExtensions
{
    /// <summary>
    /// 是否 audio.cpp 的模型：归音频引擎，不进 llama 引擎
    /// </summary>
    /// <param name="kind">种类</param>
    /// <returns>是音频模型返回 true</returns>
    public static bool IsAudio(this ELocalModelKind kind)
    {
        return kind is ELocalModelKind.SpeechSynthesis or ELocalModelKind.SpeechRecognition
            or ELocalModelKind.OtherAudio;
    }
}

/// <summary>
/// 按 GGUF 元数据判定本地模型种类
/// </summary>
public static class LocalModelKindClassifier
{
    private const long RankPooling = 4; // llama.cpp LLAMA_POOLING_TYPE_RANK

    private static readonly HashSet<string> EncoderArchitectures = new(StringComparer.OrdinalIgnoreCase)
    {
        "bert",
        "nomic-bert",
        "nomic-bert-moe",
        "jina-bert-v2",
        "jina-bert-v3",
        "xlm-roberta",
        "mpnet",
        "t5encoder"
    };

    /// <summary>
    /// 判定种类
    /// </summary>
    /// <param name="header">GGUF 文件头</param>
    /// <returns>模型种类</returns>
    public static ELocalModelKind Classify(GGufHeader header)
    {
        string architecture = header.GetString("general.architecture");
        if (string.Equals(architecture, AudioCppSpec.Architecture, StringComparison.OrdinalIgnoreCase))
            return ClassifyAudioCpp(header);

        return Classify(architecture, header.GetInteger(architecture + ".pooling_type"));
    }

    /// <summary>
    /// audio.cpp 自家打包的 GGUF：种类由内嵌 model_spec 的 category 决定；
    /// 老包缺 category 时按 family 后缀 _tts / _asr 推断。llama 引擎读不了这类文件，认不出的也绝不归对话
    /// </summary>
    private static ELocalModelKind ClassifyAudioCpp(GGufHeader header)
    {
        AudioCppSpec.Info? spec = AudioCppSpec.TryParse(header.GetString("audiocpp.model_spec.json"));
        // 老包内嵌的 spec 可能连 family 都没有（kokoro 实测如此），这时退到单独的 family 键推断
        string category = spec is { Category.Length: > 0 }
            ? spec.Category
            : AudioCppSpec.InferCategory(AudioCppSpec.ReadFamily(header));

        return category switch
        {
            "tts" => ELocalModelKind.SpeechSynthesis,
            "asr" => ELocalModelKind.SpeechRecognition,
            _ => ELocalModelKind.OtherAudio
        };
    }

    /// <summary>
    /// 判定种类
    /// </summary>
    /// <param name="architecture">general.architecture</param>
    /// <param name="poolingType">&lt;arch&gt;.pooling_type，缺省为 null</param>
    /// <returns>模型种类</returns>
    public static ELocalModelKind Classify(string architecture, long? poolingType)
    {
        if (string.Equals(architecture, "clip", StringComparison.OrdinalIgnoreCase)) return ELocalModelKind.Projector;
        if (poolingType == RankPooling) return ELocalModelKind.Reranker;
        // 与对话模型同架构的嵌入模型（如 Qwen3-Embedding）只能靠 pooling_type 认出来
        if (EncoderArchitectures.Contains(architecture)
            || architecture.Contains("embed", StringComparison.OrdinalIgnoreCase)
            || poolingType > 0)
            return ELocalModelKind.Embedding;
        return ELocalModelKind.Chat;
    }
}
