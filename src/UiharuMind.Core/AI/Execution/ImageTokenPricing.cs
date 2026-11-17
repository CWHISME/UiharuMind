/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 一张内联图片按多少 token 计——<b>按模型取口径</b>。
///
/// 原来只有一个数（「宽 × 高 / 750」× 长边上限），是「最贵的一家 × 最大的尺寸」推出来的<b>天花板</b>，
/// 却被当成了通行价：DeepSeek 的视觉接口不论尺寸，每张图上限 <b>1024</b>（官方文档：更大的图先归一化到
/// 总像素约 1300×1300，2000×2000 与 5000×5000 计价相同），按 3278 计就是 3.2 倍高估。
///
/// 这个方向与「宁可偏高」的理由不是一回事：多算出来的部分<b>不会让任何请求变短，只会让压缩提前动手</b>
/// ——压缩本身又是一次断缓存。所以认得准的口径单列，认不出的才留在偏高的一侧。
///
/// 校准（<c>TurnInputEstimate.Calibrate</c>）救不了这一头：那个系数只向上调（低于 1 一律夹回 1，
/// 见 ADR 0009），高估永远不会被它发现。所以多算的部分必须在源头治。
///
/// <b>每条口径自带一个「计价长边」</b>：发送用的是 <see cref="InlineImageLimits.SendEdge"/>（1792），
/// 而 Anthropic 收下之后会自己缩到 1568 再计——两者混用，要么对 Anthropic 虚高 30%，
/// 要么对「不缩、按像素收」的那类服务商算成低估。上界必须按各自真正计价的尺寸推（ADR 0105）。
/// </summary>
public sealed class ImageTokenPricing
{
    private readonly int _edge; //计价用的长边：超过它的先等比缩再折
    private readonly int _divisor; //每多少（缩放后的）像素折 1 token；0 表示不看尺寸,一律按上界

    private ImageTokenPricing(string family, int maxTokens, int divisor, int edge)
    {
        Family = family;
        MaxTokens = maxTokens;
        _edge = edge;
        _divisor = divisor;
    }

    /// <summary>这一条口径属于哪一族（日志用：裁图时说明按什么口径数的）</summary>
    public string Family { get; }

    /// <summary>单张图片的计价上界</summary>
    public int MaxTokens { get; }

    /// <summary>
    /// 默认口径：认不出的服务商。<b>按发送尺寸</b>取上界——我们实际发出去的就是
    /// <see cref="InlineImageLimits.SendEdge"/>，遇到一家不缩图、按像素收的服务商，按 1568 算就是低估。
    /// 认不出时只有偏高这一侧是安全的
    /// </summary>
    public static ImageTokenPricing Default { get; } = new("default",
        InlineImageLimits.MaxTokensPerImage, InlineImageLimits.TokenDivisor, InlineImageLimits.SendEdge);

    /// <summary>
    /// Anthropic 那一族：超过 1568 由<b>服务端自己缩</b>再计，按发送尺寸算会凭空多三成
    /// </summary>
    public static ImageTokenPricing Anthropic { get; } = new("anthropic",
        InlineImageLimits.MaxEdge * InlineImageLimits.MaxEdge / InlineImageLimits.TokenDivisor,
        InlineImageLimits.TokenDivisor, InlineImageLimits.MaxEdge);

    /// <summary>
    /// DeepSeek 的视觉接口：每张图上限 1024 token，且与尺寸无关（先归一化到总像素约 1300×1300 再计）。
    /// 它没有公布逐像素公式，只给了上限与归一化目标，所以统一取上限：官方那两个例子（2000×2000 与
    /// 5000×5000）本来就都是上限，而真实进入历史的图（720p 起、长边已缩）也都贴着它
    /// </summary>
    public static ImageTokenPricing DeepSeek { get; } = new("deepseek", 1024, 0, 0);

    /// <summary>
    /// 认得的族。顺序有意义：<b>具体标记在前</b>，命中即返回（将来若有「转卖某家但按另一家口径计」
    /// 的网关，把它的标记排在前面即可）
    /// </summary>
    private static readonly (string Marker, ImageTokenPricing Pricing)[] Known =
    [
        ("deepseek", DeepSeek),
        ("claude", Anthropic),
        ("anthropic", Anthropic),
    ];

    /// <summary>
    /// 按模型名取口径
    /// </summary>
    /// <param name="modelName">模型名（可能带 <c>(OCG)</c> 之类的渠道前缀）</param>
    /// <returns>该模型的口径；认不出时回落 <see cref="Default"/></returns>
    public static ImageTokenPricing For(string? modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName)) return Default;
        foreach ((string marker, ImageTokenPricing pricing) in Known)
        {
            if (modelName.Contains(marker, StringComparison.OrdinalIgnoreCase)) return pricing;
        }

        return Default;
    }

    /// <summary>
    /// 一张已知尺寸的图按多少 token 计：超过计价长边的先等比缩（服务端也会这么做），
    /// 按 <see cref="_divisor"/> 折算后封顶 <see cref="MaxTokens"/>
    /// </summary>
    /// <param name="width">宽（像素）</param>
    /// <param name="height">高（像素）</param>
    /// <returns>token 数；尺寸无效时按上界</returns>
    public int Estimate(int width, int height)
    {
        if (_divisor <= 0) return MaxTokens; //不看尺寸的一族
        if (width <= 0 || height <= 0) return MaxTokens;

        double scale = Math.Min(1, _edge / (double)Math.Max(width, height));
        return (int)Math.Min(MaxTokens, Math.Ceiling(width * scale * (height * scale) / _divisor));
    }

    /// <summary>
    /// 按图片字节头里的尺寸计；读不出尺寸（不是 PNG/JPEG/WebP 或头残缺）按上界
    /// </summary>
    /// <param name="image">图片字节</param>
    /// <returns>token 数</returns>
    public int Estimate(ReadOnlySpan<byte> image)
    {
        //不看尺寸的一族连头都不必解析
        if (_divisor <= 0) return MaxTokens;
        return ImageFormats.TryReadSize(image, out int width, out int height)
            ? Estimate(width, height)
            : MaxTokens;
    }
}
