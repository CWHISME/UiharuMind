using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>筛选下拉的一项。<see cref="Value"/> 为 null 表示「全部」</summary>
/// <param name="Value">筛选值（模型名 / 会话标识）</param>
/// <param name="Label">显示文本</param>
public sealed record UsageOptionViewData(string? Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>KPI 卡的一张</summary>
/// <param name="Title">卡名</param>
/// <param name="Value">主数值</param>
/// <param name="Sub">副信息（可能为空）</param>
/// <param name="Tag">语义色：Success / Warning / Danger / 空</param>
public sealed record UsageKpiCardViewData(string Title, string Value, string Sub, string Tag = "");

/// <summary>排行里的一行</summary>
/// <param name="Label">模型名 / 供应商名</param>
/// <param name="ValueText">主数值（token / 次数 / 金额）</param>
/// <param name="PercentText">占比</param>
/// <param name="Ratio">0..1，画进度条用</param>
/// <param name="Tip">悬停明细（思考占比、请求数、费用等）</param>
/// <param name="IsOther">是不是「其他」那一行（灰显）</param>
public sealed record UsageRankItemViewData(string Label, string ValueText, string PercentText, double Ratio,
    string Tip, bool IsOther = false);

/// <summary>失败原因分布的一行</summary>
/// <param name="Label">错误类别</param>
/// <param name="CountText">次数</param>
/// <param name="DetailText">平均重试 / 带状态码的条数</param>
/// <param name="IsCancel">用户取消（灰显，标注不计错误率）</param>
public sealed record UsageFailureItemViewData(string Label, string CountText, string DetailText, bool IsCancel);

/// <summary>明细表的一行（已格式化；点击会话钻取用 <see cref="SessionId"/>）</summary>
public sealed class UsageDetailItemViewData
{
    /// <summary>取自 <see cref="UsageDetailRow.Id"/>，翻页也不重复</summary>
    public long Id { get; init; }

    /// <summary>会话标识（点击钻取）</summary>
    public string SessionId { get; init; } = "";

    /// <summary>会话短写法</summary>
    public string SessionText { get; init; } = "";

    /// <summary>本地时间</summary>
    public string TimeText { get; init; } = "";

    /// <summary>模型名</summary>
    public string ModelName { get; init; } = "";

    /// <summary>输入 token</summary>
    public string InputText { get; init; } = "";

    /// <summary>输出 token</summary>
    public string OutputText { get; init; } = "";

    /// <summary>缓存命中（占输入的百分比）</summary>
    public string CacheText { get; init; } = "";

    /// <summary>总延迟</summary>
    public string LatencyText { get; init; } = "";

    /// <summary>估算费用；未定价显示「未公布」/本地显示「不计价」</summary>
    public string CostText { get; init; } = "";

    /// <summary>结果：成功 / 错误类别</summary>
    public string ResultText { get; init; } = "";

    /// <summary>成功与否（决定结果列的颜色）</summary>
    public bool Ok { get; init; }

    /// <summary>失败行的补充说明（status / retry / detail），成功行为空</summary>
    public string FailedDetailText { get; init; } = "";

    /// <summary>悬停提示：完整时间、会话、错误详情</summary>
    public string Tip { get; init; } = "";

    /// <summary>失败行才显示补充那一行</summary>
    public bool HasFailedDetail => FailedDetailText.Length > 0;

    /// <summary>把一条明细行转成显示行</summary>
    /// <param name="row">查询结果</param>
    /// <param name="costText">费用列文本（调用方按「本地 / 未定价 / 有价」三种口径给）</param>
    /// <returns>显示行</returns>
    public static UsageDetailItemViewData From(UsageDetailRow row, string costText)
    {
        string result = row.Ok ? Loc.Text(LangKey.UsageResultOk) : UsageFormat.ErrorKind(row.ErrorKind);
        string failedDetail = row.Ok
            ? ""
            : string.Join(" · ", new[]
            {
                row.StatusCode is { } status ? $"HTTP {status}" : "",
                row.RetryCount > 0 ? $"retry {row.RetryCount}" : "",
                row.ErrorDetail ?? "",
            }.Where(x => x.Length > 0));

        double? hitRate = row.InputTokens > 0 ? (double)row.CachedRead / row.InputTokens : null;
        return new UsageDetailItemViewData
        {
            Id = row.Id,
            SessionId = row.SessionId,
            SessionText = UsageFormat.ShortSession(row.SessionId),
            TimeText = UsageFormat.Timestamp(row.Ts),
            ModelName = row.ModelName.Length > 0 ? row.ModelName : "—",
            InputText = UsageFormat.TokensExact(row.InputTokens),
            OutputText = UsageFormat.TokensExact(row.OutputTokens),
            CacheText = row.CachedRead > 0 ? UsageFormat.Percent(hitRate) : "—",
            LatencyText = UsageFormat.Duration(row.LatencyMs),
            CostText = costText,
            ResultText = result,
            Ok = row.Ok,
            FailedDetailText = failedDetail,
            Tip = BuildTip(row, hitRate),
        };
    }

    private static string BuildTip(UsageDetailRow row, double? hitRate)
    {
        List<string> parts =
        [
            UsageFormat.Timestamp(row.Ts),
            $"session {row.SessionId}",
            $"in {UsageFormat.TokensExact(row.InputTokens)} · out {UsageFormat.TokensExact(row.OutputTokens)}",
        ];
        if (row.CachedRead > 0) parts.Add($"cache read {UsageFormat.TokensExact(row.CachedRead)} ({UsageFormat.Percent(hitRate)})");
        if (row.CachedWrite is { } cachedWrite) parts.Add($"cache write {UsageFormat.TokensExact(cachedWrite)}");
        if (row.ReasoningTokens > 0) parts.Add($"reasoning {UsageFormat.TokensExact(row.ReasoningTokens)}");
        if (row.FirstTokenMs is { } firstToken) parts.Add($"first token {UsageFormat.Duration(firstToken)}");
        if (row.RetryCount > 0) parts.Add($"retries {row.RetryCount}");
        if (row.ErrorDetail is { } detail) parts.Add(detail);
        return string.Join('\n', parts);
    }
}

/// <summary>命中率时间线上的一个点（会话视图）</summary>
/// <param name="Index">第几发（从 1 起）</param>
/// <param name="HitRate">0..1；没有输入时为 null</param>
/// <param name="Label">X 轴刻度</param>
public sealed record UsageTimelinePoint(int Index, double? HitRate, string Label);

/// <summary>排行指标</summary>
public enum EUsageRankMetric
{
    Tokens,
    Requests,
    Cost
}

/// <summary>趋势图指标</summary>
public enum EUsageTrendMetric
{
    /// <summary>默认：token 柱 + 费用线（双轴）</summary>
    TokensAndCost,

    /// <summary>只画 token</summary>
    Tokens,

    /// <summary>只画费用</summary>
    Cost,

    /// <summary>平均延迟（只统计成功请求）</summary>
    Latency,

    /// <summary>错误率（不含用户取消）</summary>
    ErrorRate,

    /// <summary>会话视图：请求级缓存命中率时间线</summary>
    SessionHitRate
}

/// <summary>时间范围预设</summary>
public enum EUsageRangeKind
{
    Today,
    Last7Days,
    Last30Days,
    ThisMonth,
    Custom
}

/// <summary>会话视图的头部摘要</summary>
/// <param name="SessionId">会话标识</param>
/// <param name="SummaryText">请求数与费用</param>
/// <param name="RangeText">首末时间</param>
/// <param name="DropHint">缓存骤降点提示（没有则为空）</param>
public sealed record UsageSessionSummaryViewData(string SessionId, string SummaryText, string RangeText,
    string DropHint);
