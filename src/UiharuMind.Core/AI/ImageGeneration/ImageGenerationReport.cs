namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 一张已落到手里的图
/// </summary>
/// <param name="Bytes">图片字节</param>
/// <param name="MediaType">按编码头认定的 MIME 类型</param>
public sealed record GeneratedImage(byte[] Bytes, string MediaType);

/// <summary>
/// 回退时被跳过的一家。只记「这一家用不了」的那种——没配、不支持编辑不算回退，不报
/// </summary>
/// <param name="Name">生图模型名</param>
/// <param name="Reason">原因</param>
public sealed record SkippedImageModel(string Name, string Reason);

/// <summary>生图模型此刻能不能接单</summary>
public enum EImageModelState
{
    /// <summary>配好了，没在熔断</summary>
    Ready,

    /// <summary>地址或模型 id 没填</summary>
    NotConfigured,

    /// <summary>连续「用不了」触发熔断，暂时跳过</summary>
    Cooling,
}

/// <summary>
/// 生图模型的状态
/// </summary>
/// <param name="State">状态</param>
/// <param name="LastError">最近一次「用不了」的原因；没有为 null</param>
public sealed record ImageModelStatus(EImageModelState State, string? LastError);

/// <summary>走完一遍回退链的结果</summary>
public sealed class ImageGenerationReport
{
    /// <summary>出图的那一家；失败时是停在的那一家，一家都没试成则为 null</summary>
    public string? ModelName { get; init; }

    /// <summary>产出的图，失败时为空</summary>
    public IReadOnlyList<GeneratedImage> Images { get; init; } = Array.Empty<GeneratedImage>();

    /// <summary>服务端改写后的提示词</summary>
    public string? RevisedPrompt { get; init; }

    /// <summary>失败原因；成功则为 null</summary>
    public ImageFailure? Failure { get; init; }

    /// <summary>真回退了才有：跳过了谁、为什么</summary>
    public IReadOnlyList<SkippedImageModel> Skipped { get; init; } = Array.Empty<SkippedImageModel>();
}
