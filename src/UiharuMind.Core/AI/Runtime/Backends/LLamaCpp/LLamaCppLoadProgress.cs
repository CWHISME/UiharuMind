namespace UiharuMind.Core.AI.Runtime.Backends;

/// <summary>
/// 按 llama-server（-lv 4）的加载日志认阶段，给出进度。读权重那一段最长却一行不打，所以它前后两步拉得最开
/// </summary>
public static class LLamaCppLoadProgress
{
    private static readonly (string Mark, float Progress)[] Milestones =
    [
        ("device_info:", 0.08f), //后端就绪（Metal 首次要先编着色器，好几秒）
        ("load_model: loading model", 0.12f),
        ("load_tensors: loading model tensors", 0.2f),
        ("model buffer size", 0.65f), //权重读完
        ("llama_kv_cache: size", 0.75f),
        ("sched_reserve: graph", 0.82f),
        ("warming up the model", 0.85f),
        ("load_model: initializing", 0.92f),
        ("llama_server: model loaded", 0.95f)
    ];

    /// <summary>
    /// 某行日志标志着到了哪一步
    /// </summary>
    /// <param name="line">日志行</param>
    /// <returns>那一步的进度；不是阶段标志为 null</returns>
    public static float? Milestone(string line)
    {
        foreach ((string mark, float progress) in Milestones)
            if (line.Contains(mark, StringComparison.Ordinal))
                return progress;
        return null;
    }
}
