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
    Projector
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
        return Classify(architecture, header.GetInteger(architecture + ".pooling_type"));
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
