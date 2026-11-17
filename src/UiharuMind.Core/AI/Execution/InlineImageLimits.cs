/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

namespace UiharuMind.Core.AI.Execution;

/// <summary>
/// 内联给模型的图片的硬约束：缩到多大、按多少 token 计。
///
/// <b>缩放的尺寸与计价的尺寸刻意不是同一个数</b>（<see cref="SendEdge"/> 与 <see cref="MaxEdge"/>）：
/// 计价要按<b>各服务商真正计价的那个尺寸</b>——Anthropic 超过 1568 由服务端自己缩，
/// 而认不出的服务商我们得按<b>发送</b>尺寸算才不至于低估；发送则按<b>能吃满像素的那一家</b>——
/// DeepSeek 先把总像素归一到约 1300×1300（1.69M），1568 见方只有 1.38M，白丢 18% 的像素。
/// 两者混用一个数，必然有一头吃亏（ADR 0105）。
///
/// 之所以放在 Core 而不是紧挨着真正做缩放的 <c>ConversationImageDownscaler</c>（在 App 项目），
/// 是因为压缩策略在 Core，而 Core 看不见 App——两边各写一个字面量迟早无声漂移。
/// </summary>
public static class InlineImageLimits
{
    /// <summary>
    /// <b>Anthropic 口径</b>的长边：超过它服务端自己会缩，所以按它算出来的 token 不低于它实际收的费
    /// </summary>
    public const int MaxEdge = 1568;

    /// <summary>
    /// <b>实际发出去</b>的长边上限，由 <c>ConversationImageDownscaler</c> 执行。
    ///
    /// 比 <see cref="MaxEdge"/> 高：16:9 的截图在 1792 下是 1792×1008 ≈ 1.81M 像素，正好越过 DeepSeek
    /// 的归一化目标（总像素约 1.69M）——它会自己缩到目标，我们拿到的是它的满像素；
    /// 而 1568 只有 1.38M，等于每张图白丢 18%，这一截缩下去就再也放不回来。
    /// 对 Anthropic 那边只是多花一点上传（它照旧缩到 1568），token 一分不多（它的口径按 <see cref="MaxEdge"/>）
    /// </summary>
    public const int SendEdge = 1792;

    /// <summary>
    /// 每多少像素折 1 token。Anthropic 的「宽 × 高 / 750」是最贵的逐像素口径，
    /// 其余按像素收的都在它之下；放在这里是为了让上界只有一个来源
    /// </summary>
    public const int TokenDivisor = 750;

    /// <summary>
    /// <b>默认口径</b>下每张内联图片的计价上界：按<b>发送尺寸</b>推，认不出服务商时的安全侧。
    ///
    /// 误差方向是<b>刻意偏高</b>的：高估只会让压缩早触发一点，低估则会发出超长请求换来 400。
    /// 但它是天花板、不是通行价：认得模型时按 <see cref="ImageTokenPricing"/> 取该服务商的口径
    /// （DeepSeek 每张固定 1024，Anthropic 按 1568 推，用这一个数计都会高估）
    /// </summary>
    public const int MaxTokensPerImage = SendEdge * SendEdge / TokenDivisor;
}
