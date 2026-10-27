namespace UiharuMind.Core.AI.Status;

/// <summary>
/// 窗口阈值的判定口径。<b>纯函数、无网络</b>：它只把已经取到的数字翻译成「要提醒」这件事，
/// 与供应商无关，因此放在共享链路里，界面层不再各写一遍 0.9 / 1.0。
/// </summary>
public static class ProviderStatusRules
{
    /// <summary>接近上限的比例（含）</summary>
    public const double NearLimitRatio = 0.9;

    /// <summary>
    /// 把窗口用量翻译成提示项
    /// </summary>
    /// <param name="meters">各窗口用量</param>
    /// <returns>打满的窗口各一条 <see cref="EProviderNoticeKind.WindowExhausted"/>，否则接近上限的各一条</returns>
    public static IReadOnlyList<ProviderStatusNotice> MeterNotices(IReadOnlyList<ProviderUsageMeter> meters)
    {
        if (meters.Count == 0) return [];

        List<ProviderStatusNotice> notices = [];
        foreach (ProviderUsageMeter meter in meters)
        {
            if (meter.IsExhausted)
            {
                notices.Add(new ProviderStatusNotice(EProviderNoticeKind.WindowExhausted, Until: meter.ResetAt));
            }
            else if (meter.Percent >= NearLimitRatio)
            {
                notices.Add(new ProviderStatusNotice(EProviderNoticeKind.WindowNearLimit, Until: meter.ResetAt));
            }
        }

        return notices;
    }

    /// <summary>
    /// 取最紧张的那个窗口（打满优先，其次比例最高）
    /// </summary>
    /// <param name="meters">各窗口用量</param>
    /// <returns>最紧张的窗口；没有窗口返回 null</returns>
    public static ProviderUsageMeter? Tightest(IReadOnlyList<ProviderUsageMeter> meters)
    {
        ProviderUsageMeter? tightest = null;
        foreach (ProviderUsageMeter meter in meters)
        {
            if (tightest == null) tightest = meter;
            else if (meter.IsExhausted && !tightest.IsExhausted) tightest = meter;
            else if (meter.IsExhausted == tightest.IsExhausted && meter.Percent > tightest.Percent) tightest = meter;
        }

        return tightest;
    }
}
