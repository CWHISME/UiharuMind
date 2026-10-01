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
/// 内联给模型的图片的两条硬约束：缩到多大、按多少 token 计。
///
/// 两者是<b>推导关系而不是两个独立的数</b>，所以必须住在一起：token 上界完全由
/// <see cref="MaxEdge"/> 推出，改了尺寸上限，计价上界自动跟随。
/// 之所以放在 Core 而不是紧挨着真正做缩放的 <c>ConversationImageDownscaler</c>（在 App 项目），
/// 是因为压缩策略在 Core，而 Core 看不见 App——两边各写一个字面量迟早无声漂移。
/// </summary>
public static class InlineImageLimits
{
    /// <summary>
    /// 长边上限。主流视觉模型在此之上不会看得更清楚：Anthropic 超过它服务端自己会缩，
    /// OpenAI 更是先缩到短边 768 才切 tile，多传的像素纯属白费。
    /// </summary>
    public const int MaxEdge = 1568;

    /// <summary>
    /// 每张内联图片按多少 token 计。
    ///
    /// 取<b>最贵的口径 × 最大的尺寸</b>：Anthropic 是 <c>宽 × 高 / 750</c>，而
    /// <see cref="MaxEdge"/> 保证了任何进入历史的图都不超过 1568 见方——所以这是一个
    /// 能证明的天花板，不是拍出来的经验值（OpenAI 的 tile 口径只有它的四分之一左右）。
    ///
    /// 误差方向是<b>刻意偏高</b>的：高估只会让压缩早触发一点，低估则会发出超长请求换来 400。
    /// </summary>
    public const int MaxTokensPerImage = MaxEdge * MaxEdge / 750;

    /// <summary>
    /// 一张已知尺寸的图按多少 token 计：仍取最贵的「宽 × 高 / 750」口径，只是按这张图自己的像素算，
    /// 超过 <see cref="MaxEdge"/> 的先等比缩（服务端也会这么做），封顶 <see cref="MaxTokensPerImage"/>。
    /// 误差方向不变，仍不低于它在任何一家的实际开销
    /// </summary>
    /// <param name="width">宽（像素）</param>
    /// <param name="height">高（像素）</param>
    /// <returns>token 数；尺寸无效时按上限</returns>
    public static int EstimateTokens(int width, int height)
    {
        if (width <= 0 || height <= 0) return MaxTokensPerImage;
        double scale = Math.Min(1, MaxEdge / (double)Math.Max(width, height));
        return (int)Math.Min(MaxTokensPerImage, Math.Ceiling(width * scale * (height * scale) / 750));
    }

    /// <summary>
    /// 按图片字节头里的尺寸计；读不出尺寸（不是 PNG/JPEG/WebP 或头残缺）按上限
    /// </summary>
    /// <param name="image">图片字节</param>
    /// <returns>token 数</returns>
    public static int EstimateTokens(ReadOnlySpan<byte> image) =>
        ImageFormats.TryReadSize(image, out int width, out int height)
            ? EstimateTokens(width, height)
            : MaxTokensPerImage;
}
