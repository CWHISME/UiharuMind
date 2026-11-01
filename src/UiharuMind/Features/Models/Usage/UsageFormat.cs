using System;
using System.Globalization;
using UiharuMind.Core.AI.Usage;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 用量页的显示格式化。<b>只做数字 → 文本</b>，不碰 UI 线程也不查库，方便单测。
/// 口径：token 用 k/M 缩写；金额是 USD（不做汇率换算，ADR 0097）；
/// null 的语义是「不知道」，一律显示成「—」而不是 0
/// </summary>
public static class UsageFormat
{
    /// <summary>token 数：<c>987</c> / <c>12.3k</c> / <c>1.24M</c></summary>
    /// <param name="value">token 数</param>
    /// <returns>紧凑写法</returns>
    public static string Tokens(long value) => value switch
    {
        >= 1_000_000_000 => (value / 1_000_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "B",
        >= 1_000_000 => (value / 1_000_000d).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 1_000 => (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString(CultureInfo.InvariantCulture),
    };

    /// <summary>精确 token 数（明细表里要能对账）</summary>
    /// <param name="value">token 数</param>
    /// <returns>带千分位</returns>
    public static string TokensExact(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>金额：小额给四位小数，大额给两位；null 显示 <c>—</c></summary>
    /// <param name="value">美元金额</param>
    /// <returns>带 $ 的文本</returns>
    public static string Money(decimal? value) => value switch
    {
        null => "—",
        >= 100m => "$" + value.Value.ToString("0.00", CultureInfo.InvariantCulture),
        >= 1m => "$" + value.Value.ToString("0.000", CultureInfo.InvariantCulture),
        _ => "$" + value.Value.ToString("0.0000", CultureInfo.InvariantCulture),
    };

    /// <summary>百分比；null 显示 <c>—</c></summary>
    /// <param name="ratio">0..1 的比例</param>
    /// <returns>百分比文本</returns>
    public static string Percent(double? ratio) => ratio is { } value
        ? (value * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%"
        : "—";

    /// <summary>毫秒延迟：<c>820 ms</c> / <c>12.3 s</c>；null 显示 <c>—</c></summary>
    /// <param name="milliseconds">毫秒</param>
    /// <returns>可读时长</returns>
    public static string Duration(double? milliseconds) => milliseconds switch
    {
        null => "—",
        < 1000 => milliseconds.Value.ToString("0", CultureInfo.InvariantCulture) + " ms",
        < 60_000 => (milliseconds.Value / 1000d).ToString("0.0#", CultureInfo.InvariantCulture) + " s",
        _ => TimeSpan.FromMilliseconds(milliseconds.Value).ToString(@"m\:ss", CultureInfo.InvariantCulture),
    };

    /// <summary>本地时间（明细行与 tooltip）</summary>
    /// <param name="ts">Unix 毫秒</param>
    /// <returns><c>10-31 14:05:09</c></returns>
    public static string Timestamp(long ts) =>
        DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime().ToString("MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>趋势图 X 轴刻度：按粒度的短写法</summary>
    /// <param name="ts">桶起点，Unix 毫秒</param>
    /// <param name="granularity">粒度</param>
    /// <returns>刻度文本</returns>
    public static string BucketLabel(long ts, EUsageGranularity granularity)
    {
        DateTimeOffset local = DateTimeOffset.FromUnixTimeMilliseconds(ts).ToLocalTime();
        return granularity switch
        {
            EUsageGranularity.Hour => local.ToString("MM-dd HH:mm", CultureInfo.InvariantCulture),
            EUsageGranularity.Week => "W" + IsoWeek(local).ToString(CultureInfo.InvariantCulture),
            _ => local.ToString("MM-dd", CultureInfo.InvariantCulture),
        };
    }

    /// <summary>会话标识的短写法（完整值仍在 tooltip 与筛选里）</summary>
    /// <param name="sessionId">会话标识</param>
    /// <returns>前 8 位</returns>
    public static string ShortSession(string sessionId) =>
        sessionId.Length <= 8 ? sessionId : sessionId[..8];

    /// <summary>错误类别的显示文本（未知类别原样显示）；null 表示没记类别</summary>
    /// <param name="kind">错误类别常量</param>
    /// <returns>显示文本</returns>
    public static string ErrorKind(string? kind) => kind ?? "—";

    private static int IsoWeek(DateTimeOffset value) =>
        System.Globalization.ISOWeek.GetWeekOfYear(value.Date);
}
