namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// 失败的三类，回退只认第一类。见 ADR 0052。
/// </summary>
public enum EImageFailureKind
{
    /// <summary>这一家现在用不了（没配、熔断、连不上、5xx、限流、鉴权、模型下线）：换下一个</summary>
    Unavailable,

    /// <summary>这次请求本身不行（内容审核、参数非法、输入图读不了）：停下交还模型</summary>
    Rejected,

    /// <summary>可能已经扣费（超时、网关超时、成功却拿不到图）：停下并如实说明</summary>
    MaybeCharged,
}

/// <summary>
/// 一次失败
/// </summary>
/// <param name="Kind">类别</param>
/// <param name="Message">原因，原样给模型与日志</param>
public sealed record ImageFailure(EImageFailureKind Kind, string Message);

/// <summary>
/// 接口给回的一张图：字节或地址二选一。地址由回退链统一下载，接口格式不管落地。
/// </summary>
/// <param name="Bytes">图片字节</param>
/// <param name="Url">图片地址（多为临时链接）</param>
public sealed record ImageOutput(byte[]? Bytes, string? Url);

/// <summary>一个接口格式问一次的结果：成功有图，失败有原因</summary>
public sealed class ImageOutcome
{
    /// <summary>产出的图，失败时为空</summary>
    public IReadOnlyList<ImageOutput> Images { get; init; } = Array.Empty<ImageOutput>();

    /// <summary>服务端改写后的提示词，没给则为 null</summary>
    public string? RevisedPrompt { get; init; }

    /// <summary>失败原因；成功则为 null</summary>
    public ImageFailure? Failure { get; init; }

    /// <summary>
    /// 构造一次失败
    /// </summary>
    /// <param name="kind">类别</param>
    /// <param name="message">原因</param>
    /// <returns>失败结果</returns>
    public static ImageOutcome Fail(EImageFailureKind kind, string message) =>
        new() { Failure = new ImageFailure(kind, message) };
}
