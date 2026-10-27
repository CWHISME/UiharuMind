namespace UiharuMind.Core.AI.Status.Providers.CommandCode;

/// <summary>
/// CC 的模型级计费规则：<b>纯本地按时钟算，零网络</b>。
///
/// 现在的规则（来自官方价格页，不是接口）：
/// DeepSeek V4 系列在工作日的 01:00–04:00、06:00–10:00（UTC）按 ×2 计价；
/// <c>*-free</c> / <c>:free</c> 的模型不计费；<c>minimax-m3</c> 促销加倍额度。
///
/// 官方一调就要改这里——所以规则只许写在这个类里，别散到共享链路去。
/// <b>判定必须用 UTC</b>（窗口是按 UTC 划的），显示才用本地时区。
/// </summary>
public sealed class CommandCodeBillingCalendar : IModelBillingRule
{
    private const double PeakMultiplier = 2;

    // 高峰期在 UTC 下的小时区间，左闭右开
    private static readonly (int Start, int End)[] PeakWindowsUtc = [(1, 4), (6, 10)];

    // 促销倍率表：模型标识片段 → 倍率。匹配不到就什么都不显示（宁可少提示，不可错提示）
    private static readonly (string Fragment, double Multiplier)[] Promotions = [("minimax-m3", 2)];

    /// <summary>
    /// 描述此刻这个模型的计费状态
    /// </summary>
    /// <param name="context">取数输入（只用模型标识）</param>
    /// <param name="now">当前时刻</param>
    /// <returns>提示项；没有可说的返回空列表</returns>
    public IReadOnlyList<ProviderStatusNotice> Describe(ProviderStatusContext context, DateTimeOffset now)
    {
        List<ProviderStatusNotice> notices = [];

        if (IsPeakPricedModel(context.ModelId) && PeakUntil(now) is { } until)
        {
            notices.Add(new ProviderStatusNotice(EProviderNoticeKind.PeakPricing, PeakMultiplier, until));
        }

        if (IsFreeModel(context.ModelId))
        {
            notices.Add(new ProviderStatusNotice(EProviderNoticeKind.FreeModel));
        }
        else if (PromotionOf(context.ModelId) is { } multiplier)
        {
            notices.Add(new ProviderStatusNotice(EProviderNoticeKind.PromoMultiplier, multiplier));
        }

        return notices;
    }

    /// <summary>
    /// 此刻是否处在高峰计价窗口内
    /// </summary>
    /// <param name="now">任意时区的时刻</param>
    /// <returns>在窗口内返回窗口结束时刻（UTC），否则 null</returns>
    public static DateTimeOffset? PeakUntil(DateTimeOffset now)
    {
        DateTimeOffset utc = now.ToUniversalTime();
        if (!IsWorkday(utc.DayOfWeek)) return null;

        foreach ((int start, int end) in PeakWindowsUtc)
        {
            if (utc.Hour < start || utc.Hour >= end) continue;
            return new DateTimeOffset(utc.Year, utc.Month, utc.Day, end, 0, 0, TimeSpan.Zero);
        }

        return null;
    }

    /// <summary>按小时计价（有高峰概念）的模型：DeepSeek V4 系列</summary>
    private static bool IsPeakPricedModel(string modelId) =>
        modelId.Contains("deepseek-v4", StringComparison.OrdinalIgnoreCase);

    private static bool IsFreeModel(string modelId) =>
        modelId.EndsWith(":free", StringComparison.OrdinalIgnoreCase) ||
        modelId.EndsWith("-free", StringComparison.OrdinalIgnoreCase);

    private static double? PromotionOf(string modelId)
    {
        foreach ((string fragment, double multiplier) in Promotions)
        {
            if (modelId.Contains(fragment, StringComparison.OrdinalIgnoreCase)) return multiplier;
        }

        return null;
    }

    private static bool IsWorkday(DayOfWeek day) =>
        day is not (DayOfWeek.Saturday or DayOfWeek.Sunday);
}
