namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 采样参数（各家族推荐的不一样，见生图模型的档位）
/// </summary>
/// <param name="Steps">步数</param>
/// <param name="CfgScale">CFG（蒸馏过的模型多为 1）</param>
/// <param name="Sampler">采样器；null 交给 sd.cpp</param>
/// <param name="Scheduler">调度器；null 交给 sd.cpp</param>
/// <param name="FlowShift">flow shift；null 交给 sd.cpp</param>
/// <param name="DistilledGuidance">蒸馏引导（FLUX 一类）；null 交给 sd.cpp</param>
public sealed record SdCppSampling(
    int Steps,
    float CfgScale,
    string? Sampler = null,
    string? Scheduler = null,
    float? FlowShift = null,
    float? DistilledGuidance = null);

/// <summary>
/// sd.cpp 认的采样器与调度器名（include/stable-diffusion.h），参数窗的下拉用
/// </summary>
public static class SdCppSamplers
{
    /// <summary>
    /// 采样器
    /// </summary>
    public static IReadOnlyList<string> Samplers { get; } =
    [
        "euler", "euler_a", "heun", "dpm2", "dpm++2s_a", "dpm++2m", "dpm++2mv2", "ipndm", "ipndm_v", "lcm",
        "ddim_trailing", "tcd", "res_multistep", "res_2s", "er_sde"
    ];

    /// <summary>
    /// 调度器
    /// </summary>
    public static IReadOnlyList<string> Schedulers { get; } =
        ["discrete", "karras", "exponential", "ays", "gits", "sgm_uniform", "simple", "smoothstep", "kl_optimal", "beta"];
}

/// <summary>
/// 交给本地生图引擎的一次出图
/// </summary>
/// <param name="Prompt">提示词</param>
/// <param name="Width">宽（像素）</param>
/// <param name="Height">高（像素）</param>
/// <param name="Sampling">采样参数</param>
public sealed record SdCppImageRequest(string Prompt, int Width, int Height, SdCppSampling Sampling)
{
    /// <summary>
    /// 负向提示词
    /// </summary>
    public string NegativePrompt { get; init; } = "";

    /// <summary>
    /// 参考图（改图时给，第 1 张为主图）
    /// </summary>
    public IReadOnlyList<byte[]> ReferenceImages { get; init; } = [];

    /// <summary>
    /// 种子；负数为随机
    /// </summary>
    public long Seed { get; init; } = -1;
}

/// <summary>
/// 出图走到哪一步
/// </summary>
public enum ESdCppStage
{
    /// <summary>起引擎、读权重</summary>
    Starting,

    /// <summary>排在别的出图后面</summary>
    Queued,

    /// <summary>在采样</summary>
    Generating
}

/// <summary>
/// 出图进度
/// </summary>
/// <param name="Stage">阶段</param>
/// <param name="Step">当前步（采样时）</param>
/// <param name="TotalSteps">这一轮共几步</param>
/// <param name="Preview">中间预览图（PNG）；没有为 null</param>
public sealed record SdCppProgress(ESdCppStage Stage, int Step = 0, int TotalSteps = 0, byte[]? Preview = null);

/// <summary>
/// sd-server 拒收这次请求（参数不合法等）：换说法或换参数再来，换家没用
/// </summary>
public sealed class SdCppRequestException(string message) : Exception(message);

/// <summary>
/// 任务跑失败了（显存不够、模型和参数对不上等）
/// </summary>
/// <param name="code">错误码</param>
/// <param name="message">原因</param>
public sealed class SdCppGenerationException(string code, string message) : Exception(message)
{
    /// <summary>
    /// 错误码，如 generation_failed
    /// </summary>
    public string Code { get; } = code;
}

/// <summary>
/// 生图模型缺文件（被删了、还没下完）
/// </summary>
/// <param name="model">模型名</param>
/// <param name="files">缺的文件</param>
public sealed class SdCppModelMissingException(string model, IReadOnlyList<string> files)
    : Exception($"Image model '{model}' is missing files: {string.Join(", ", files)}")
{
    /// <summary>
    /// 缺的文件
    /// </summary>
    public IReadOnlyList<string> Files { get; } = files;
}
