namespace UiharuMind.Core.AI.Usage;

/// <summary>
/// KPI 聚合：把 <see cref="UsageAggregateRow"/> 的一批分组行<b>加权</b>合成全局数字。
///
/// 均值只能这样算：分组行上再 AVG 一次等于按 (模型, 供应商) 组合加权，和按请求直接算
/// 对不上（跑得多的小模型会被大模型的行拖平）。所以 SQL 只给和，除法在这里做一次
/// </summary>
public sealed record UsageTotals(
    long InputTokens,
    long CachedRead,
    long OutputTokens,
    long ReasoningTokens,
    int Requests,
    int SuccessRequests,
    int Failures,
    int ServiceFailures,
    int Retries,
    int CachedWriteRequests,
    int OfficialPricedRequests,
    int ModelsDevPricedRequests,
    int UnpricedRequests,
    int LocalRequests,
    decimal? CostUsd,
    decimal? CacheSavedUsd,
    long LatencySumMs,
    long FirstTokenSumMs)
{
    /// <summary>一行都没有</summary>
    public static UsageTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, null, null, 0, 0);

    /// <summary>输入 + 输出</summary>
    public long TotalTokens => InputTokens + OutputTokens;

    /// <summary>缓存命中率（0..1）；没有输入时为 null</summary>
    public double? CacheHitRate => InputTokens > 0 ? Math.Clamp((double)CachedRead / InputTokens, 0, 1) : null;

    /// <summary>单请求平均 token（分母是全部请求，失败行只带它已收到的量）；没有请求时为 null</summary>
    public double? TokensPerRequest => Requests > 0 ? (double)TotalTokens / Requests : null;

    /// <summary>平均首 token 延迟（毫秒）；没有测到时为 null</summary>
    public double? AvgFirstTokenMs => SuccessRequests > 0 && FirstTokenSumMs > 0
        ? (double)FirstTokenSumMs / SuccessRequests
        : null;

    /// <summary>平均总延迟（毫秒）；没有测到时为 null</summary>
    public double? AvgLatencyMs => SuccessRequests > 0 && LatencySumMs > 0
        ? (double)LatencySumMs / SuccessRequests
        : null;

    /// <summary>错误率（0..1），分子不含用户取消；没有请求时为 null</summary>
    public double? ErrorRate => Requests > 0 ? (double)ServiceFailures / Requests : null;

    /// <summary>平均重试次数；没有请求时为 null</summary>
    public double? AvgRetries => Requests > 0 ? (double)Retries / Requests : null;

    /// <summary>有没有任何请求</summary>
    public bool HasRequests => Requests > 0;

    /// <summary>有没有任何一条上报了缓存写入量</summary>
    public bool HasCachedWrite => CachedWriteRequests > 0;

    /// <summary>按官网价目计价</summary>
    public bool HasOfficialPrice => OfficialPricedRequests > 0;

    /// <summary>按 models.dev 计价</summary>
    public bool HasModelsDevPrice => ModelsDevPricedRequests > 0;

    /// <summary>价格是否来自多家（界面显示「多来源」）</summary>
    public bool HasMixedPriceSources => HasOfficialPrice && HasModelsDevPrice;

    /// <summary>是不是只有本地模型（费用卡据此显示「本地 · 不计价」而不是「未公布」）</summary>
    public bool OnlyLocal => Requests > 0 && LocalRequests == Requests;

    /// <summary>
    /// 把一批分组行合成全局数字。费用与缓存节省按「只要有一行算得出就相加」处理，
    /// 全都没价时保持 null——null 的语义是「不知道」，不是「零」
    /// </summary>
    /// <param name="rows">分组行</param>
    /// <returns>KPI 数字</returns>
    public static UsageTotals From(IEnumerable<UsageAggregateRow> rows)
    {
        long input = 0;
        long cached = 0;
        long output = 0;
        long reasoning = 0;
        int requests = 0;
        int successes = 0;
        int failures = 0;
        int serviceFailures = 0;
        int retries = 0;
        int cachedWrite = 0;
        int official = 0;
        int modelsDev = 0;
        int unpriced = 0;
        int local = 0;
        long latencySum = 0;
        long firstTokenSum = 0;
        decimal? cost = null;
        decimal? saved = null;

        foreach (UsageAggregateRow row in rows)
        {
            input += row.InputTokens;
            cached += row.CachedRead;
            output += row.OutputTokens;
            reasoning += row.ReasoningTokens;
            requests += row.Requests;
            successes += row.SuccessRequests;
            failures += row.Failures;
            serviceFailures += row.ServiceFailures;
            retries += row.Retries;
            cachedWrite += row.CachedWriteRequests;
            official += row.OfficialPricedRequests;
            modelsDev += row.ModelsDevPricedRequests;
            unpriced += row.UnpricedRequests;
            local += row.LocalRequests;
            latencySum += row.LatencySumMs;
            firstTokenSum += row.FirstTokenSumMs;
            if (row.CostUsd is { } rowCost) cost = (cost ?? 0m) + rowCost;
            if (row.CacheSavedUsd is { } rowSaved) saved = (saved ?? 0m) + rowSaved;
        }

        return new UsageTotals(input, cached, output, reasoning, requests, successes, failures,
            serviceFailures, retries, cachedWrite, official, modelsDev, unpriced, local, cost, saved, latencySum,
            firstTokenSum);
    }
}

/// <summary>
/// 把「只含有数据的桶」补齐成连续的桶序列。趋势图必须画连续的轴，
/// 中间没有请求的那几格要落在 0 上，否则看不出一段安静期
/// </summary>
public static class UsageTrendFill
{
    /// <summary>点数硬上限；超过它的范围应当降一档粒度再查（否则图表会卡）</summary>
    public const int MaxPoints = 1500;

    /// <summary>
    /// 计算一个范围的桶起点序列（含起点，不含终点之后的桶）
    /// </summary>
    /// <param name="range">时间范围</param>
    /// <param name="zone">本地时区（按它的周一/零点对齐）</param>
    /// <returns>桶起点毫秒序列</returns>
    public static IReadOnlyList<long> Buckets(UsageRange range, TimeZoneInfo zone)
    {
        long bucketMs = UsageBucket.SizeOf(range.Granularity);
        long anchor = UsageBucket.AnchorFor(range, zone);
        if (bucketMs <= 0 || range.ToTs <= range.FromTs) return [];

        List<long> buckets = [];
        for (long ts = anchor; ts < range.ToTs && buckets.Count <= MaxPoints; ts += bucketMs)
        {
            if (ts + bucketMs <= range.FromTs) continue; //起点之前的空桶不要
            buckets.Add(ts);
        }

        return buckets;
    }

    /// <summary>
    /// 补齐空桶。返回的序列与 <paramref name="buckets"/> 等长，没有数据的格子是 0 值
    /// </summary>
    /// <param name="points">SQL 出来的趋势点（按桶升序，可以缺桶）</param>
    /// <param name="buckets">完整的桶起点序列</param>
    /// <returns>连续的趋势点</returns>
    public static IReadOnlyList<UsageTrendPoint> Fill(IReadOnlyList<UsageTrendPoint> points,
        IReadOnlyList<long> buckets)
    {
        if (buckets.Count == 0) return [];
        Dictionary<long, UsageTrendPoint> byBucket = new(points.Count);
        foreach (UsageTrendPoint point in points) byBucket[point.BucketTs] = point;

        List<UsageTrendPoint> filled = new(buckets.Count);
        foreach (long bucket in buckets)
        {
            filled.Add(byBucket.TryGetValue(bucket, out UsageTrendPoint? point)
                ? point
                : new UsageTrendPoint(bucket, 0, null, 0, 0, 0, 0));
        }

        return filled;
    }

    /// <summary>
    /// 这个范围在该粒度下会有多少桶（界面据此决定要不要降一档）
    /// </summary>
    /// <param name="range">时间范围</param>
    /// <returns>桶数</returns>
    public static int CountOf(UsageRange range) =>
        (int)Math.Min(int.MaxValue, Math.Max(0, (range.ToTs - range.FromTs) / UsageBucket.SizeOf(range.Granularity)));
}

/// <summary>
/// 分桶的锚点与时区口径。<b>不用 <c>strftime</c></b>：SQLite 的时区陷阱（本地时区要靠字符串修饰符，
/// 且与运行时时区库不一定同源）比整数除法麻烦得多，这里只做整数分桶
/// </summary>
public static class UsageBucket
{
    /// <summary>粒度对应的毫秒数</summary>
    /// <param name="granularity">粒度</param>
    /// <returns>桶宽（毫秒）</returns>
    public static long SizeOf(EUsageGranularity granularity) => granularity switch
    {
        EUsageGranularity.Hour => 3_600_000L,
        EUsageGranularity.Week => 604_800_000L,
        _ => 86_400_000L,
    };

    /// <summary>
    /// 桶序列的锚点：范围内第一个桶的起点，按<b>本地时区</b>对齐
    /// （时 → 整点，日 → 零点，周 → 周一零点）。
    /// SQL 侧用 <c>((ts - @anchor) / @bucketMs) * @bucketMs + @anchor</c> 分桶，
    /// 这样跨日/跨周都不会串桶，也不需要把时区规则写进 SQL
    /// </summary>
    /// <param name="range">时间范围</param>
    /// <param name="zone">本地时区</param>
    /// <returns>锚点（Unix 毫秒）</returns>
    public static long AnchorFor(UsageRange range, TimeZoneInfo zone)
    {
        DateTimeOffset from = DateTimeOffset.FromUnixTimeMilliseconds(range.FromTs);
        DateTimeOffset local = TimeZoneInfo.ConvertTime(from, zone);
        DateTime date = local.DateTime.Date;
        if (range.Granularity == EUsageGranularity.Hour) date = date.AddHours(local.Hour);
        //周桶按本地周一零点对齐：DayOfWeek 把周日算 0，本地口径下周一才是第一天
        if (range.Granularity == EUsageGranularity.Week)
            date = date.AddDays(-(((int)local.DayOfWeek + 6) % 7));

        DateTimeOffset anchored = new(date, local.Offset);
        return anchored.ToUnixTimeMilliseconds();
    }

    /// <summary>某个时刻属于哪个桶（与 SQL 的分桶公式逐字同构，单测对拍用）</summary>
    /// <param name="ts">时刻（Unix 毫秒）</param>
    /// <param name="anchor">锚点（<see cref="AnchorFor"/>）</param>
    /// <param name="granularity">粒度</param>
    /// <returns>桶起点</returns>
    public static long Of(long ts, long anchor, EUsageGranularity granularity)
    {
        long size = SizeOf(granularity);
        long offset = ts - anchor;
        long index = offset >= 0 ? offset / size : (offset - size + 1) / size; //向负无穷取整
        return anchor + index * size;
    }
}
