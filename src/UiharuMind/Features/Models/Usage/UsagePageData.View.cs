using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 从聚合结果拼视图：KPI 六卡、排行与占比、失败分布、明细行、命中率时间线与图表 series。
/// 所有格式化口径集中在这里，XAML 只绑定
/// </summary>
public partial class UsagePageData
{
    private UsageRange _range; //最近一次查询用的范围（改指标时重建 series 要用同一份）

    private void BuildKpis()
    {
        Kpis.Clear();
        string none = Loc.Text(LangKey.UsageKpiNone);

        //Token 总量：单请求均值突然翻倍往往提示上下文在悄悄变大
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiTokens),
            _totals.HasRequests ? UsageFormat.Tokens(_totals.TotalTokens) : none,
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiTokensSub, UsageFormat.Tokens(_totals.InputTokens),
                    UsageFormat.Tokens(_totals.OutputTokens), UsageFormat.Tokens((long)(_totals.TokensPerRequest ?? 0)))
                : ""));

        //请求次数：失败里去掉用户取消——那是用户自己按的，不是服务失败
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiRequests),
            _totals.HasRequests ? _totals.Requests.ToString() : none,
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiRequestsSub, _totals.ServiceFailures,
                    (_totals.AvgRetries ?? 0).ToString("0.0"))
                : ""));

        //缓存命中率 + 缓存节省（缓存价未公布时记 0，宁不高估）
        decimal saved = _totals.CacheSavedUsd ?? 0m;
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiCacheHit),
            UsageFormat.Percent(_totals.CacheHitRate),
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiCacheSub, UsageFormat.Percent(_totals.CacheHitRate),
                    UsageFormat.Tokens(_totals.InputTokens), saved > 0 ? UsageFormat.Money(saved) : "$0.00")
                : ""));

        //估算费用：全未定价显示「未公布」，全本地显示「本地 · 不计价」——都不是 $0.00
        string costValue = _totals.OnlyLocal
            ? Loc.Text(LangKey.UsageKpiCostLocal)
            : _totals.CostUsd == null
                ? Loc.Text(LangKey.UsageKpiCostNoPrice)
                : UsageFormat.Money(_totals.CostUsd);
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiCost),
            costValue,
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiCostSub, PriceSourceText(_totals), _totals.UnpricedRequests)
                : ""));

        //平均首 token + 平均总延迟（同一个分母：成功请求数）
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiFirstToken),
            UsageFormat.Duration(_totals.AvgFirstTokenMs),
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiFirstTokenSub, UsageFormat.Duration(_totals.AvgLatencyMs))
                : ""));

        //错误率：>5% 警告、>20% 危险
        double? errorRate = _totals.ErrorRate;
        string tag = errorRate switch
        {
            > 0.2 => "Danger",
            > 0.05 => "Warning",
            _ => "",
        };
        Kpis.Add(new UsageKpiCardViewData(
            Loc.Text(LangKey.UsageKpiErrorRate),
            UsageFormat.Percent(errorRate),
            _totals.HasRequests
                ? Loc.Text(LangKey.UsageKpiErrorRateSub, _totals.ServiceFailures,
                    _totals.Failures - _totals.ServiceFailures)
                : "",
            tag));
    }

    private string PriceSourceText(UsageTotals totals)
    {
        if (totals.HasMixedPriceSources) return Loc.Text(LangKey.UsagePriceMixed);
        if (totals.HasOfficialPrice) return Loc.Text(LangKey.UsagePriceOfficial);
        if (totals.HasModelsDevPrice) return Loc.Text(LangKey.UsagePriceModelsDev);
        return Loc.Text(LangKey.UsageKpiNone);
    }

    private void BuildFailures(IReadOnlyList<FailureKindStat> stats)
    {
        Failures.Clear();
        foreach (FailureKindStat stat in stats)
        {
            string label = stat.ErrorKind == null
                ? Loc.Text(LangKey.UsageFailureUnknown)
                : UsageFormat.ErrorKind(stat.ErrorKind);
            string detail = stat.ErrorKind == UsageErrorKinds.Cancel
                ? Loc.Text(LangKey.UsageFailureCancelNote)
                : stat.WithStatus > 0
                    ? $"HTTP ×{stat.WithStatus}"
                    : "";
            Failures.Add(new UsageFailureItemViewData(
                label,
                stat.AvgRetryCount is { } retry
                    ? Loc.Text(LangKey.UsageFailureRow, stat.Count, retry.ToString("0.0"))
                    : Loc.Text(LangKey.UsageFailureRowNoRetry, stat.Count),
                detail,
                stat.ErrorKind == UsageErrorKinds.Cancel));
        }
    }

    private void BuildDetails(UsageDetailPage page)
    {
        Details.Clear();
        foreach (UsageDetailRow row in page.Rows)
        {
            string cost = row.IsLocal
                ? Loc.Text(LangKey.UsageDetailLocalCost)
                : row.CostUsd == null
                    ? Loc.Text(LangKey.UsageDetailUnpriced)
                    : UsageFormat.Money(row.CostUsd);
            Details.Add(UsageDetailItemViewData.From(row, cost));
        }
    }

    private IReadOnlyList<UsageTimelinePoint> BuildTimeline(IReadOnlyList<UsageDetailRow> rows)
    {
        List<UsageTimelinePoint> points = new(rows.Count);
        for (int i = 0; i < rows.Count; i++)
        {
            UsageDetailRow row = rows[i];
            double? hit = row.InputTokens > 0 ? (double)row.CachedRead / row.InputTokens : null;
            points.Add(new UsageTimelinePoint(i + 1, hit, "#" + (i + 1)));
        }

        return points;
    }

    private void BuildSessionSummary(IReadOnlyList<UsageDetailRow> rows)
    {
        if (!IsSessionView || rows.Count == 0)
        {
            SessionSummary = null;
            return;
        }

        decimal? cost = null;
        foreach (UsageDetailRow row in rows)
        {
            if (row.CostUsd is { } value) cost = (cost ?? 0m) + value;
        }

        int dropIndex = FindCacheDrop(_timelinePoints);
        string dropHint = dropIndex > 0
            ? Loc.Text(LangKey.UsageTrendDropHint, dropIndex)
            : Loc.Text(LangKey.UsageTrendNoDrop);
        string costText = UsageFormat.Money(cost);
        SessionSummary = new UsageSessionSummaryViewData(
            SessionOption?.Value ?? "",
            Loc.Text(LangKey.UsageSessionSummary, rows.Count, costText),
            Loc.Text(LangKey.UsageSessionRange, UsageFormat.Timestamp(rows[0].Ts),
                UsageFormat.Timestamp(rows[^1].Ts)),
            dropHint);
        TrendHint = dropHint;
    }

    /// <summary>
    /// 命中率骤降点：从高位（≥50%）掉到接近 0（&lt;10%）的那一条。
    /// 精确到「哪条消息导致分叉」需要请求内容，<c>usage_record</c> 不存，不做
    /// </summary>
    private static int FindCacheDrop(IReadOnlyList<UsageTimelinePoint> points)
    {
        for (int i = 1; i < points.Count; i++)
        {
            double? previous = points[i - 1].HitRate;
            double? current = points[i].HitRate;
            if (previous == null || current == null) continue;
            if (previous >= 0.5 && current < 0.1) return points[i].Index;
        }

        return 0;
    }

    private void RebuildRank()
    {
        RankItems.Clear();
        if (_totals.Requests == 0)
        {
            ShareSeries = [];
            return;
        }

        EUsageDimension dimension = Enum.TryParse(RankDimensionOption?.Value, true, out EUsageDimension parsed)
            ? parsed
            : EUsageDimension.Model;
        EUsageRankMetric metric = Enum.TryParse(RankMetricOption?.Value, true, out EUsageRankMetric metricParsed)
            ? metricParsed
            : EUsageRankMetric.Tokens;

        //同一维度上的行要合并（供应商维度会把多个模型加在一起）
        Dictionary<string, RankAccumulator> groups = new(StringComparer.Ordinal);
        foreach (UsageAggregateRow row in AllRows)
        {
            string label = dimension == EUsageDimension.Provider
                ? row.IsLocal ? "local" : row.ProviderKey.Length > 0 ? row.ProviderKey : "—"
                : row.ModelName.Length > 0 ? row.ModelName : "—";
            if (!groups.TryGetValue(label, out RankAccumulator? accumulator))
            {
                accumulator = new RankAccumulator();
                groups[label] = accumulator;
            }

            accumulator.Add(row);
        }

        List<(string Label, RankAccumulator Value)> sorted = [.. groups
            .Select(pair => (Label: pair.Key, Value: pair.Value))
            .OrderByDescending(x => x.Value.MetricOf(metric))];
        double total = sorted.Sum(x => Math.Max(0, x.Value.MetricOf(metric)));

        //排行与占比图同一份排序：一遍出两样，不再各走一遍 sorted
        UsageChartPalette palette = UsageChartPalette.Current;
        List<UsageRankItemViewData> items = [];
        List<ISeries> series = [];
        double shareOthers = 0;
        double others = 0;
        int othersRequests = 0;
        decimal? othersCost = null;
        long othersTokens = 0;
        long othersReasoning = 0;
        long othersOutput = 0;
        for (int i = 0; i < sorted.Count; i++)
        {
            (string label, RankAccumulator value) = sorted[i];
            double metricValue = Math.Max(0, value.MetricOf(metric));

            //占比饼图取前几块，碎成一片读不了
            if (total > 0)
            {
                if (i < ShareTopCount) series.Add(ShareSlice(label, metricValue, palette.Series[i % palette.Series.Count]));
                else shareOthers += metricValue;
            }

            if (items.Count < RankTopCount)
            {
                items.Add(BuildRankItem(label, value, metric, total));
                continue;
            }

            others += metricValue;
            othersRequests += value.Requests;
            othersTokens += value.Input + value.Output;
            othersReasoning += value.Reasoning;
            othersOutput += value.Output;
            if (value.CostUsd is { } cost) othersCost = (othersCost ?? 0m) + cost;
        }

        if (others > 0)
        {
            string label = Loc.Text(LangKey.UsageRankOthers);
            string valueText = metric switch
            {
                EUsageRankMetric.Requests => othersRequests.ToString(),
                EUsageRankMetric.Cost => UsageFormat.Money(othersCost),
                _ => UsageFormat.Tokens(othersTokens),
            };
            items.Add(new UsageRankItemViewData(label, valueText, UsageFormat.Percent(total > 0 ? others / total : null),
                total > 0 ? others / total : 0,
                BuildRankTip(label, othersRequests, othersCost, othersReasoning, othersOutput), true));
        }

        if (shareOthers > 0) series.Add(ShareSlice(Loc.Text(LangKey.UsageRankOthers), shareOthers, palette.Muted));

        foreach (UsageRankItemViewData item in items) RankItems.Add(item);
        ShareSeries = [.. series];
    }

    private static ISeries ShareSlice(string label, double value, SolidColorPaint fill) => new PieSeries<double>
    {
        Values = [value],
        Name = label,
        Fill = fill,
        Stroke = null,
        DataLabelsPaint = null,
    };

    private UsageRankItemViewData BuildRankItem(string label, RankAccumulator value, EUsageRankMetric metric,
        double total)
    {
        double metricValue = Math.Max(0, value.MetricOf(metric));
        string valueText = metric switch
        {
            EUsageRankMetric.Requests => value.Requests.ToString(),
            EUsageRankMetric.Cost => UsageFormat.Money(value.CostUsd),
            _ => UsageFormat.Tokens(value.Input + value.Output),
        };
        return new UsageRankItemViewData(label, valueText,
            UsageFormat.Percent(total > 0 ? metricValue / total : null),
            total > 0 ? metricValue / total : 0,
            BuildRankTip(label, value.Requests, value.CostUsd, value.Reasoning, value.Output));
    }

    private string BuildRankTip(string label, int requests, decimal? cost, long reasoning, long output)
    {
        string tip = Loc.Text(LangKey.UsageRankRowTip, label, requests, UsageFormat.Money(cost));
        if (reasoning > 0 && output > 0) tip += "\n" + Loc.Text(LangKey.UsageRankReasoningTip, UsageFormat.Percent((double)reasoning / output));
        return tip;
    }

    /// <summary>聚合行（排行要按维度再分组，先留着原样）</summary>
    private IReadOnlyList<UsageAggregateRow> AllRows { get; set; } = [];

    private sealed class RankAccumulator
    {
        public long Input;
        public long Output;
        public long Reasoning;
        public int Requests;
        public decimal? CostUsd;

        public void Add(UsageAggregateRow row)
        {
            Input += row.InputTokens;
            Output += row.OutputTokens;
            Reasoning += row.ReasoningTokens;
            Requests += row.Requests;
            if (row.CostUsd is { } cost) CostUsd = (CostUsd ?? 0m) + cost;
        }

        /// <summary>排行指标的取值；未定价的费用按 0 参与排序（显示仍是「—」）</summary>
        public double MetricOf(EUsageRankMetric metric) => metric switch
        {
            EUsageRankMetric.Requests => Requests,
            EUsageRankMetric.Cost => (double)(CostUsd ?? 0m),
            _ => Input + Output,
        };
    }
}
