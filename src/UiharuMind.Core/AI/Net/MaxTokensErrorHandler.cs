/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Text.RegularExpressions;

namespace UiharuMind.Core.AI.Net;

/// <summary>
/// 识别服务端对 <c>max_tokens</c> 参数返回的 400 错误，并提取其声明的合法上限。
///
/// 各家兼容服务的措辞不同（实测有
/// <c>"Parameter: max_tokens ... field MaxTokens invalid, should be in [1, 384000]"</c>
/// 这类），判据放宽为：错误链里存在消息同时含 <c>max_tokens</c>（不区分大小写、带不带
/// 下划线都认，覆盖 <c>MaxTokens</c> 这类写法）与 <c>should be in [1, N]</c> 区间。命中后取区间的
/// 上限 N 作为配置应回退到的合法值——它最接近用户「想要更大输出」的原意。
/// </summary>
internal static class MaxTokensErrorHandler
{
    private static readonly Regex RangePattern = new(
        @"should be in \[\d+, (\d+)\]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// 沿异常链寻找 max_tokens 被服务端拒绝的错误，并返回其声明的合法上限。
    /// </summary>
    /// <param name="exception">顶层异常；沿 <see cref="Exception.InnerException"/> 逐层找</param>
    /// <param name="upperBound">命中的合法上限；未命中时为 0</param>
    /// <returns>命中为 true</returns>
    public static bool TryGetUpperBound(Exception exception, out int upperBound)
    {
        upperBound = 0;
        for (Exception? e = exception; e != null; e = e.InnerException)
        {
            string message = e.Message;
            if (!MentionsMaxTokens(message)) continue;
            Match match = RangePattern.Match(message);
            if (!match.Success) continue;
            if (!int.TryParse(match.Groups[1].Value, out int bound) || bound <= 0) continue;

            upperBound = bound;
            return true;
        }

        return false;
    }

    private static bool MentionsMaxTokens(string message) =>
        message.Contains("max_tokens", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("maxtokens", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 是否为 max_tokens 被服务端拒绝的错误（不关心合法上限的具体值）。
    /// </summary>
    /// <param name="exception">顶层异常</param>
    /// <returns>命中为 true</returns>
    public static bool IsMaxTokensError(Exception exception) => TryGetUpperBound(exception, out _);
}