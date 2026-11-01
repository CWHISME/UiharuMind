namespace UiharuMind.Core.AI.Usage;

/// <summary>趋势图的分桶粒度</summary>
public enum EUsageGranularity
{
    /// <summary>按小时</summary>
    Hour,

    /// <summary>按天</summary>
    Day,

    /// <summary>按周（本地周一零点为桶起点）</summary>
    Week
}

/// <summary>排行的分组维度</summary>
public enum EUsageDimension
{
    /// <summary>按模型（model_name）</summary>
    Model,

    /// <summary>按供应商（provider_key）</summary>
    Provider
}

/// <summary>明细分页的排序列。<b>白名单</b>：用户输入永远不拼进 SQL</summary>
public enum EUsageDetailSortColumn
{
    Time,
    Model,
    InputTokens,
    OutputTokens,
    CachedRead,
    Latency,
    Cost
}

/// <summary>排序方向</summary>
public enum EUsageSortDirection
{
    Descending,
    Ascending
}

/// <summary>
/// 一次查询的时间范围，<b>左闭右开</b>（<c>from &lt;= ts &lt; to</c>）。
/// 界面上的「今日 / 近 7 天 / 本月」都换算成这里的两个毫秒戳，统计口径只有这一处
/// </summary>
/// <param name="FromTs">起点（含），Unix 毫秒</param>
/// <param name="ToTs">终点（不含），Unix 毫秒</param>
/// <param name="Granularity">趋势分桶粒度</param>
public readonly record struct UsageRange(long FromTs, long ToTs, EUsageGranularity Granularity);

/// <summary>KPI、排行、失败分布、趋势共用的查询条件</summary>
/// <param name="Range">时间范围</param>
/// <param name="ModelFilter">只看某个模型（model_name）；null 表示不限</param>
/// <param name="SessionFilter">只看某个会话；null 表示不限</param>
public sealed record UsageQuery(UsageRange Range, string? ModelFilter = null, string? SessionFilter = null);

/// <summary>
/// 聚合出行（KPI 与排行共用）。
/// <para>
/// 均值不在这里算，只带 <see cref="LatencySumMs"/> / <see cref="FirstTokenSumMs"/> 与
/// <see cref="SuccessRequests"/>：在分组行上再 AVG 一次等于按 (模型, 供应商) 组合加权，
/// 与按请求直接算对不上。见 <see cref="UsageTotals.From(System.Collections.Generic.IEnumerable{UsageAggregateRow})"/>
/// </para>
/// </summary>
/// <param name="ModelName">模型名（用户可见名，也是配置字典键）</param>
/// <param name="ProviderKey">定价表键；本地与未登记为空串</param>
/// <param name="IsLocal">是否本地模型</param>
/// <param name="InputTokens">输入 token 合计（含命中缓存的部分）</param>
/// <param name="CachedRead">其中命中前缀缓存的部分</param>
/// <param name="OutputTokens">输出 token 合计</param>
/// <param name="ReasoningTokens">其中思考 token</param>
/// <param name="Requests">请求次数（含失败）</param>
/// <param name="Failures">失败次数（含取消）</param>
/// <param name="ServiceFailures">失败里不算用户取消的那些（错误率的分子）</param>
/// <param name="Retries">客户端内部重发次数合计</param>
/// <param name="CachedWriteRequests">上报了缓存写入 token 的请求数（全为 0 时界面降级为说明条）</param>
/// <param name="OfficialPricedRequests">按官网价目计价的请求数</param>
/// <param name="ModelsDevPricedRequests">按 models.dev 计价的请求数</param>
/// <param name="UnpricedRequests">成功请求里没算出费用的次数（本地模型不计入）</param>
/// <param name="LocalRequests">本地模型请求次数</param>
/// <param name="CostUsd">估算费用；全为未定价时为 null（不是 0）</param>
/// <param name="CacheSavedUsd">缓存节省；缓存价未公布时为 null（不是 0）</param>
/// <param name="LatencySumMs">成功请求的总延迟合计</param>
/// <param name="FirstTokenSumMs">成功请求的首 token 延迟合计</param>
/// <param name="SuccessRequests">成功请求数（均值的分母）</param>
public sealed record UsageAggregateRow(
    string ModelName,
    string ProviderKey,
    bool IsLocal,
    long InputTokens,
    long CachedRead,
    long OutputTokens,
    long ReasoningTokens,
    int Requests,
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
    long FirstTokenSumMs,
    int SuccessRequests)
{
    /// <summary>输入 + 输出，KPI 与排行排序都用它</summary>
    public long TotalTokens => InputTokens + OutputTokens;
}

/// <summary>一次明细查询：分页 + 排序 + 筛选</summary>
/// <param name="Range">时间范围</param>
/// <param name="ModelFilter">只看某个模型；null 不限</param>
/// <param name="SessionFilter">只看某个会话；null 不限</param>
/// <param name="OnlyFailed">只看失败行</param>
/// <param name="SortColumn">排序列（白名单）</param>
/// <param name="SortDirection">排序方向</param>
/// <param name="Limit">取多少行</param>
/// <param name="Offset">跳过多少行</param>
public sealed record UsageDetailQuery(
    UsageRange Range,
    string? ModelFilter = null,
    string? SessionFilter = null,
    bool OnlyFailed = false,
    EUsageDetailSortColumn SortColumn = EUsageDetailSortColumn.Time,
    EUsageSortDirection SortDirection = EUsageSortDirection.Descending,
    int Limit = 50,
    int Offset = 0);

/// <summary>明细行（分页结果）</summary>
public sealed record UsageDetailRow(
    long Id,
    long Ts,
    string SessionId,
    string ModelName,
    string ProviderKey,
    bool IsLocal,
    long InputTokens,
    long CachedRead,
    long? CachedWrite,
    long OutputTokens,
    long ReasoningTokens,
    int? LatencyMs,
    int? FirstTokenMs,
    bool Ok,
    string? ErrorKind,
    string? ErrorDetail,
    int? StatusCode,
    int RetryCount,
    decimal? CostUsd);

/// <summary>一页明细</summary>
/// <param name="Rows">这一页的行</param>
/// <param name="Total">当前筛选条件下的总行数（分页器据此算页数）</param>
public sealed record UsageDetailPage(IReadOnlyList<UsageDetailRow> Rows, int Total);

/// <summary>趋势图上的一个点</summary>
/// <param name="BucketTs">桶起点，Unix 毫秒（本地时区对齐）</param>
/// <param name="Tokens">输入 + 输出</param>
/// <param name="CostUsd">估算费用；全未定价时为 null</param>
/// <param name="Requests">请求次数</param>
/// <param name="LatencySumMs">成功请求的总延迟</param>
/// <param name="SuccessRequests">成功请求数</param>
/// <param name="ServiceFailures">不算取消的失败数</param>
public sealed record UsageTrendPoint(
    long BucketTs,
    long Tokens,
    decimal? CostUsd,
    int Requests,
    long LatencySumMs,
    int SuccessRequests,
    int ServiceFailures)
{
    /// <summary>该桶的平均延迟；没有成功请求时为 null</summary>
    public double? AvgLatencyMs => SuccessRequests > 0 ? (double)LatencySumMs / SuccessRequests : null;

    /// <summary>该桶的错误率（0..1）；没有请求时为 null</summary>
    public double? ErrorRate => Requests > 0 ? (double)ServiceFailures / Requests : null;
}

/// <summary>失败原因分布的一行</summary>
/// <param name="ErrorKind">错误类别（null 表示没记类别）</param>
/// <param name="Count">次数</param>
/// <param name="AvgRetryCount">平均重试次数</param>
/// <param name="WithStatus">带 HTTP 状态码的次数</param>
public sealed record FailureKindStat(string? ErrorKind, int Count, double? AvgRetryCount, int WithStatus);

/// <summary>会话汇总（筛选栏的会话下拉）</summary>
public sealed record SessionUsageSummary(string SessionId, int Requests, long FirstTs, long LastTs, decimal? CostUsd);

/// <summary>一次查询的全部结果：KPI + 排行 + 失败分布 + 趋势</summary>
/// <param name="Rows">按 (模型, 供应商) 分组的聚合行</param>
/// <param name="Trend">按粒度分桶的趋势点（只含有数据的桶，空桶由 <see cref="UsageTrendFill"/> 补齐）</param>
/// <param name="Failures">失败原因分布</param>
public sealed record UsageSnapshot(
    IReadOnlyList<UsageAggregateRow> Rows,
    IReadOnlyList<UsageTrendPoint> Trend,
    IReadOnlyList<FailureKindStat> Failures)
{
    /// <summary>空结果（没有库、读取失败时的安全返回值）</summary>
    public static UsageSnapshot Empty { get; } = new([], [], []);
}
