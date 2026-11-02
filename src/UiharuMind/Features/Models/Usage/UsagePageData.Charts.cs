using System;
using System.Collections.Generic;
using System.Linq;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 趋势图的 code-only 构造。<b>不用 XAML 的 series</b>（它们走反射实例化，AOT 下会静默失效），
/// XAML 只绑定 <see cref="UsagePageData.TrendSeries"/> 这一组属性。
///
/// <para>
/// 默认双轴：左轴 token（柱）、右轴费用（线）——两组数量级差两个数量级，同轴会把费用压成一条平线。
/// 指标切换（token / 费用 / 平均延迟 / 错误率）只是换 series 集合，控件不变；
/// 选中会话时切成「请求级命中率时间线」。
/// </para>
/// </summary>
public partial class UsagePageData
{
    /// <summary>X 轴最多显示这么多个刻度（其余留空，避免挤成一团）</summary>
    private const int MaxAxisLabels = 10;

    /// <summary>
    /// 折线的平滑度（0 = 折线，1 = 最圆）。<b>取 LiveCharts 官方默认的 0.65</b>：
    /// 库里那几个示例的「贝塞尔感」就是它给的。注意两点以上才弯得起来——
    /// 只有两个非空点的一段，控制点落在同一条直线上，再大的平滑度也还是直线
    /// </summary>
    private const double Smoothness = 0.65;

    private EUsageTrendMetric CurrentTrendMetric =>
        Enum.TryParse(TrendMetricOption?.Value, true, out EUsageTrendMetric parsed)
            ? parsed
            : EUsageTrendMetric.TokensAndCost;

    private void RebuildTrend()
    {
        RebuildTrendCore();
        ChartsRebuilt?.Invoke();
    }

    private void RebuildTrendCore()
    {
        UsageChartPalette palette = UsageChartPalette.Current;
        if (IsSessionView)
        {
            BuildSessionTrend(palette);
            return;
        }

        IReadOnlyList<long> buckets = UsageTrendFill.Buckets(_range, TimeZoneInfo.Local);
        IReadOnlyList<UsageTrendPoint> points = UsageTrendFill.Fill(_trendPoints, buckets);
        IsEmptyTrend = points.All(x => x.Requests == 0);
        string[] labels = BuildBucketLabels(buckets);
        EUsageTrendMetric metric = CurrentTrendMetric;

        TrendXAxes = [CreateCategoryAxis(labels, palette)];
        switch (metric)
        {
            case EUsageTrendMetric.Cost:
                TrendSeries = [CreateCostSeries(points, palette, 0)];
                TrendYAxes = [CreateCostAxis(points, palette, atEnd: false)];
                break;
            case EUsageTrendMetric.Latency:
                TrendSeries = [CreateLatencySeries(points, palette, 0)];
                TrendYAxes = [CreateValueAxis(palette, DurationLabel, separators: true)];
                break;
            case EUsageTrendMetric.ErrorRate:
                TrendSeries = [CreateErrorRateSeries(points, palette, 0)];
                TrendYAxes = [CreateValueAxis(palette, PercentLabel, separators: true)];
                break;
            case EUsageTrendMetric.Tokens:
                TrendSeries = [CreateTokenSeries(points, palette, 0)];
                TrendYAxes = [CreateValueAxis(palette, TokenLabel, separators: true)];
                break;
            default:
                TrendSeries =
                [
                    CreateTokenSeries(points, palette, 0),
                    CreateCostSeries(points, palette, 1),
                ];
                //左轴 token、右轴费用：两把尺子各管一条线
                TrendYAxes =
                [
                    CreateValueAxis(palette, TokenLabel, separators: true),
                    CreateCostAxis(points, palette, atEnd: true),
                ];
                break;
        }
    }

    private void BuildSessionTrend(UsageChartPalette palette)
    {
        IReadOnlyList<UsageTimelinePoint> points = _timelinePoints;
        IsEmptyTrend = points.Count == 0;
        int step = Math.Max(1, (int)Math.Ceiling(points.Count / (double)MaxAxisLabels));
        string[] labels = points.Select((point, index) => index % step == 0 ? point.Label : "").ToArray();

        List<ISeries> series =
        [
            new LineSeries<double?>
            {
                Values = points.Select(x => x.HitRate is { } hit ? hit * 100 : (double?)null).ToArray(),
                Name = Loc.Text(LangKey.UsageTrendSeriesHitRate),
                Stroke = palette.HitRateStroke,
                Fill = null,
                GeometrySize = 0,
                LineSmoothness = 0.15,
                ScalesYAt = 0,
            },
        ];

        //骤降点标一个点：packed 数组里只在那一条有值，其余 null
        int dropIndex = FindCacheDrop(points);
        if (dropIndex > 0)
        {
            double?[] markers = new double?[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i].Index != dropIndex) continue;
                markers[i] = points[i].HitRate is { } hit ? hit * 100 : 0;
                break;
            }

            series.Add(new LineSeries<double?>
            {
                Values = markers,
                Name = Loc.Text(LangKey.UsageTrendSeriesDrop),
                Stroke = null,
                Fill = palette.ErrorStroke,
                GeometrySize = 12,
                LineSmoothness = 0,
                ScalesYAt = 0,
            });
        }

        TrendSeries = [.. series];
        TrendXAxes = [CreateCategoryAxis(labels, palette)];
        TrendYAxes =
        [
            new Axis
            {
                MinLimit = 0,
                MaxLimit = 100,
                Labeler = PercentLabel,
                TextSize = 11,
                LabelsPaint = palette.AxisLabel,
                SeparatorsPaint = palette.Separator,
            },
        ];
    }

    private static ISeries CreateTokenSeries(IReadOnlyList<UsageTrendPoint> points, UsageChartPalette palette,
        int scale) =>
        new ColumnSeries<double?>
        {
            Values = points.Select(x => (double?)x.Tokens).ToArray(),
            Name = Loc.Text(LangKey.UsageTrendSeriesTokens),
            Fill = palette.TokenFill,
            Stroke = null,
            MaxBarWidth = 26,
            ScalesYAt = scale,
        };

    private static ISeries CreateCostSeries(IReadOnlyList<UsageTrendPoint> points, UsageChartPalette palette,
        int scale) =>
        new LineSeries<decimal?>
        {
            //空桶落 0 而不是断线：那天没请求就是没花钱，是已知的 0，不是「不知道」；
            //有请求但全没定价的桶仍然是 null，那才是真的没有数
            Values = points.Select(x => x.Requests == 0 ? 0m : x.CostUsd).ToArray(),
            Name = Loc.Text(LangKey.UsageTrendSeriesCost),
            Stroke = palette.CostStroke,
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = Smoothness,
            ScalesYAt = scale,
        };

    private static ISeries CreateLatencySeries(IReadOnlyList<UsageTrendPoint> points, UsageChartPalette palette,
        int scale) =>
        new LineSeries<double?>
        {
            Values = points.Select(x => x.AvgLatencyMs).ToArray(),
            Name = Loc.Text(LangKey.UsageTrendSeriesLatency),
            Stroke = palette.LatencyStroke,
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = Smoothness,
            ScalesYAt = scale,
        };

    private static ISeries CreateErrorRateSeries(IReadOnlyList<UsageTrendPoint> points, UsageChartPalette palette,
        int scale) =>
        new LineSeries<double?>
        {
            Values = points.Select(x => x.ErrorRate is { } rate ? rate * 100 : (double?)null).ToArray(),
            Name = Loc.Text(LangKey.UsageTrendSeriesErrorRate),
            Stroke = palette.ErrorStroke,
            Fill = null,
            GeometrySize = 0,
            LineSmoothness = Smoothness,
            ScalesYAt = scale,
        };

    private static Axis CreateCategoryAxis(IReadOnlyList<string> labels, UsageChartPalette palette) => new()
    {
        Labels = [.. labels],
        MinStep = 1,
        ForceStepToMin = true,
        TextSize = 11,
        LabelsPaint = palette.AxisLabel,
        //X 轴不要网格线：点一多就是一层灰幕
        SeparatorsPaint = null,
    };

    private static Axis CreateValueAxis(UsageChartPalette palette, Func<double, string> labeler, bool separators,
        bool atEnd = false, double minLimit = 0) => new()
    {
        //默认从 0 起：只显示 200k 以上会把「安静的那几天」裁成空白，看起来像没数据。
        //费用轴是例外，底线要压到 0 以下，见 CreateCostAxis
        MinLimit = minLimit,
        Labeler = labeler,
        TextSize = 11,
        LabelsPaint = palette.AxisLabel,
        SeparatorsPaint = separators ? palette.Separator : null,
        Position = atEnd ? AxisPosition.End : AxisPosition.Start,
    };

    /// <summary>
    /// 费用轴。<b>底线压在 0 下面一点点</b>，不是为了好看：
    /// 空桶落 0 之后，那条平线正好落在绘图区底边上，2px 的描边被裁掉一半，
    /// 又和 $0 这条网格线叠在一起，看上去就是一截发黑的线。
    /// 留出 5% 的余量让它整条描边都在绘图区内。刻度起点会向下取整到整数档
    /// （<c>start = Truncate(min / step) * step</c>），所以标签还是 $0 / $0.5 / $1 这些整数，不会冒出 -$0.02
    /// </summary>
    private static Axis CreateCostAxis(IReadOnlyList<UsageTrendPoint> points, UsageChartPalette palette, bool atEnd)
    {
        decimal maxCost = points.Count == 0 ? 0m : points.Max(x => x.CostUsd ?? 0m);
        return CreateValueAxis(palette, MoneyLabel, separators: !atEnd, atEnd: atEnd,
            minLimit: -(double)maxCost * 0.05);
    }

    /// <summary>桶多时只给每 k 个桶一个刻度文字，其余留空（刻度线本来也不画）</summary>
    private string[] BuildBucketLabels(IReadOnlyList<long> buckets)
    {
        int step = Math.Max(1, (int)Math.Ceiling(buckets.Count / (double)MaxAxisLabels));
        string[] labels = new string[buckets.Count];
        for (int i = 0; i < buckets.Count; i++)
        {
            labels[i] = i % step == 0 ? UsageFormat.BucketLabel(buckets[i], _range.Granularity) : "";
        }

        return labels;
    }

    private static string TokenLabel(double value) => UsageFormat.Tokens((long)Math.Max(0, value));

    private static string MoneyLabel(double value) =>
        "$" + value.ToString(value >= 1 ? "0.##" : value >= 0.01 ? "0.###" : "0.####",
            System.Globalization.CultureInfo.InvariantCulture);

    private static string DurationLabel(double value) => UsageFormat.Duration(value);

    private static string PercentLabel(double value) => value.ToString("0.#") + "%";
}
