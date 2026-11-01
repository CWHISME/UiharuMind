using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Generated;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Models.Usage;

/// <summary>
/// 查询与状态机：范围换算、粒度选择、分页查询、以及「晚到结果丢弃」。
/// 拆在这个文件里是为了让 <see cref="UsagePageData"/> 本身只剩状态与命令
/// </summary>
public partial class UsagePageData
{
    /// <summary>一次整页查询的产物（在后台线程拼好，回 UI 线程一次性应用）</summary>
    private sealed record LoadResult(
        UsageSnapshot Snapshot,
        UsageDetailPage Details,
        IReadOnlyList<UsageDetailRow> Timeline,
        IReadOnlyList<SessionUsageSummary> Sessions,
        IReadOnlyList<string> Models,
        bool HasAnyRecords);

    private void InitializeOptions()
    {
        //初值一律不触发重查：第一次加载交给 OnEnable，构造期同步发出去的那几轮只会被 generation 丢掉
        _suppressReload = true;
        try
        {
            RangeOptions.Add(new UsageOptionViewData(nameof(EUsageRangeKind.Today), Loc.Text(LangKey.UsageRangeToday)));
            RangeOptions.Add(new UsageOptionViewData(nameof(EUsageRangeKind.Last7Days),
                Loc.Text(LangKey.UsageRangeLast7Days)));
            RangeOptions.Add(new UsageOptionViewData(nameof(EUsageRangeKind.Last30Days),
                Loc.Text(LangKey.UsageRangeLast30Days)));
            RangeOptions.Add(new UsageOptionViewData(nameof(EUsageRangeKind.ThisMonth),
                Loc.Text(LangKey.UsageRangeThisMonth)));
            RangeOptions.Add(new UsageOptionViewData(nameof(EUsageRangeKind.Custom),
                Loc.Text(LangKey.UsageRangeCustom)));

            GranularityOptions.Add(new UsageOptionViewData("", Loc.Text(LangKey.UsageGranularityAuto)));
            GranularityOptions.Add(new UsageOptionViewData(nameof(EUsageGranularity.Hour),
                Loc.Text(LangKey.UsageGranularityHour)));
            GranularityOptions.Add(new UsageOptionViewData(nameof(EUsageGranularity.Day),
                Loc.Text(LangKey.UsageGranularityDay)));
            GranularityOptions.Add(new UsageOptionViewData(nameof(EUsageGranularity.Week),
                Loc.Text(LangKey.UsageGranularityWeek)));

            TrendMetricOptions.Add(new UsageOptionViewData(nameof(EUsageTrendMetric.TokensAndCost),
                Loc.Text(LangKey.UsageTrendSeriesTokens) + " + " + Loc.Text(LangKey.UsageTrendSeriesCost)));
            TrendMetricOptions.Add(new UsageOptionViewData(nameof(EUsageTrendMetric.Tokens),
                Loc.Text(LangKey.UsageTrendSeriesTokens)));
            TrendMetricOptions.Add(new UsageOptionViewData(nameof(EUsageTrendMetric.Cost),
                Loc.Text(LangKey.UsageTrendSeriesCost)));
            TrendMetricOptions.Add(new UsageOptionViewData(nameof(EUsageTrendMetric.Latency),
                Loc.Text(LangKey.UsageTrendSeriesLatency)));
            TrendMetricOptions.Add(new UsageOptionViewData(nameof(EUsageTrendMetric.ErrorRate),
                Loc.Text(LangKey.UsageTrendSeriesErrorRate)));

            RankDimensionOptions.Add(new UsageOptionViewData(nameof(EUsageDimension.Model), Loc.Text(LangKey.UsageRankModels)));
            RankDimensionOptions.Add(new UsageOptionViewData(nameof(EUsageDimension.Provider),
                Loc.Text(LangKey.UsageRankProviders)));

            RankMetricOptions.Add(new UsageOptionViewData(nameof(EUsageRankMetric.Tokens), Loc.Text(LangKey.UsageRankTokens)));
            RankMetricOptions.Add(new UsageOptionViewData(nameof(EUsageRankMetric.Requests),
                Loc.Text(LangKey.UsageRankRequests)));
            RankMetricOptions.Add(new UsageOptionViewData(nameof(EUsageRankMetric.Cost), Loc.Text(LangKey.UsageRankCost)));

            //列表第一项必须是「全部」，否则用户选不回去
            ModelOptions.Add(new UsageOptionViewData(null, Loc.Text(LangKey.UsageAllModels)));
            SessionOptions.Add(new UsageOptionViewData(null, Loc.Text(LangKey.UsageAllSessions)));

            RangeOption = RangeOptions[1]; //默认近 7 天
            GranularityOption = GranularityOptions[0];
            TrendMetricOption = TrendMetricOptions[0];
            RankDimensionOption = RankDimensionOptions[0];
            RankMetricOption = RankMetricOptions[0];
            ModelOption = ModelOptions[0];
            SessionOption = SessionOptions[0];
        }
        finally
        {
            _suppressReload = false;
        }
    }

    /// <summary>「自定义」范围的最大跨度（天）：再长的区间只是一次全表扫描，页面也读不出新信息</summary>
    private const int MaxCustomRangeDays = 366;

    private static EUsageRangeKind RangeKindOf(UsageOptionViewData? option) =>
        Enum.TryParse(option?.Value, true, out EUsageRangeKind kind) ? kind : EUsageRangeKind.Last7Days;

    /// <summary>按预设算左闭右开区间；「近 N 天」按本地日历对齐（含今天，共 N 天）</summary>
    private UsageRange BuildRange()
    {
        DateTimeOffset now = DateTimeOffset.Now;
        DateTime today = now.Date;
        TimeSpan offset = now.Offset;
        DateTimeOffset dayStart = new(today, offset);
        DateTimeOffset from;
        DateTimeOffset to;
        switch (RangeKindOf(RangeOption))
        {
            case EUsageRangeKind.Today:
                from = dayStart;
                to = dayStart.AddDays(1);
                break;
            case EUsageRangeKind.Last30Days:
                from = dayStart.AddDays(-29);
                to = dayStart.AddDays(1);
                break;
            case EUsageRangeKind.ThisMonth:
                from = new DateTimeOffset(new DateTime(today.Year, today.Month, 1), offset);
                to = from.AddMonths(1);
                break;
            case EUsageRangeKind.Custom:
                //自定义是「含首尾两天」：起那天零点 → 止那天次日零点
                DateTime fromDate = (CustomFrom ?? today.AddDays(-6)).Date;
                DateTime toDate = (CustomTo ?? today).Date;
                from = new DateTimeOffset(fromDate, offset);
                to = new DateTimeOffset(toDate, offset).AddDays(1);
                if (to <= from) to = from.AddDays(1);
                //超长区间当场收到上限内：范围筛选只管查得到什么，不该变成一次全表扫描
                if ((to - from).TotalDays > MaxCustomRangeDays) from = to.AddDays(-MaxCustomRangeDays);
                break;
            default:
                from = dayStart.AddDays(-6);
                to = dayStart.AddDays(1);
                break;
        }

        return new UsageRange(from.ToUnixTimeMilliseconds(), to.ToUnixTimeMilliseconds(), EUsageGranularity.Day);
    }

    /// <summary>粒度：自动按跨度选，显式选择优先；超过点数上限自动降一档并给提示</summary>
    private UsageRange ResolveRange(out string hint)
    {
        UsageRange basis = BuildRange();
        long span = basis.ToTs - basis.FromTs;
        EUsageGranularity chosen;
        if (Enum.TryParse(GranularityOption?.Value, true, out EUsageGranularity explicitGranularity))
        {
            chosen = explicitGranularity;
        }
        else
        {
            chosen = span <= 2L * 86_400_000 ? EUsageGranularity.Hour
                : span <= 120L * 86_400_000 ? EUsageGranularity.Day
                : EUsageGranularity.Week;
        }

        hint = "";
        int count = UsageTrendFill.CountOf(basis with { Granularity = chosen });
        if (count > UsageTrendFill.MaxPoints)
        {
            EUsageGranularity downgraded = chosen == EUsageGranularity.Hour
                ? EUsageGranularity.Day
                : EUsageGranularity.Week;
            hint = Loc.Text(LangKey.UsageTrendThinned);
            chosen = downgraded;
        }

        return basis with { Granularity = chosen };
    }

    private UsageDetailQuery BuildDetailQuery(int limit, int offset)
    {
        UsageRange range = ResolveRange(out _);
        return new UsageDetailQuery(range, ModelOption?.Value, SessionOption?.Value, OnlyFailed, _sortColumn,
            _sortDirection, limit, offset);
    }

    /// <summary>
    /// 整页重查（范围 / 模型 / 会话 / 粒度 / 只看失败变化）
    /// </summary>
    private async Task RefreshAsync()
    {
        int generation = ++_generation;
        IsLoading = _totals.Requests == 0; //已有数据时不闪整页，只让细表转圈
        IsLoadingDetails = true;
        try
        {
            UsageRange range = ResolveRange(out string granularityHint);
            string? model = ModelOption?.Value;
            string? session = SessionOption?.Value;
            bool onlyFailed = OnlyFailed;
            EUsageDetailSortColumn sortColumn = _sortColumn;
            EUsageSortDirection sortDirection = _sortDirection;
            int offset = _pageIndex * PageSize;

            LoadResult result = await Task.Run(() =>
            {
                UsageSnapshot snapshot = _store.Query(new UsageQuery(range, model, session));
                UsageDetailPage details = _store.QueryDetails(new UsageDetailQuery(range, model, session, onlyFailed,
                    sortColumn, sortDirection, PageSize, offset));
                IReadOnlyList<UsageDetailRow> timeline = session == null
                    ? []
                    : _store.QuerySessionTimeline(session, range.FromTs, range.ToTs);
                IReadOnlyList<SessionUsageSummary> sessions = _store.ListSessions(range.FromTs, range.ToTs);
                IReadOnlyList<string> models = _store.KnownModelNames(range.FromTs, range.ToTs);
                //「从来没有过记录」要问全表，但它是单调事实：查到一次 true 之后不再查
                bool hasAny = _hasAnyKnown || _store.QueryDetails(new UsageDetailQuery(
                    new UsageRange(0, DateTimeOffset.MaxValue.ToUnixTimeMilliseconds(), EUsageGranularity.Day),
                    Limit: 1)).Total > 0;
                return new LoadResult(snapshot, details, timeline, sessions, models, hasAny);
            });

            if (generation != _generation) return; //晚到的旧结果直接丢
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                //先设本发粒度带来的提示；Apply 里的会话视图会覆盖成命中率提示
                TrendHint = granularityHint;
                Apply(result, range);
            });
        }
        catch (Exception e)
        {
            if (generation != _generation) return;
            _messages?.ShowNotification(Loc.Text(LangKey.UsageLoadFailed, e.Message),
                severity: MessageSeverity.Error);
        }
        finally
        {
            if (generation == _generation)
            {
                IsLoading = false;
                IsLoadingDetails = false;
            }
        }
    }

    /// <summary>
    /// 只重查明细（翻页 / 换排序 / 只看失败）。KPI 与排行不动，避免整页闪
    /// </summary>
    private async Task RefreshDetailsAsync()
    {
        int generation = ++_generation;
        IsLoadingDetails = true;
        try
        {
            UsageDetailQuery query = BuildDetailQuery(PageSize, _pageIndex * PageSize);
            UsageDetailPage page = await Task.Run(() => _store.QueryDetails(query));
            if (generation != _generation) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                BuildDetails(page);
                UpdatePaging(page.Total);
            });
        }
        catch (Exception e)
        {
            if (generation != _generation) return;
            _messages?.ShowNotification(Loc.Text(LangKey.UsageLoadFailed, e.Message),
                severity: MessageSeverity.Error);
        }
        finally
        {
            if (generation == _generation) IsLoadingDetails = false;
        }
    }

    private void Apply(LoadResult result, UsageRange range)
    {
        _range = range;
        AllRows = result.Snapshot.Rows;
        _totals = UsageTotals.From(result.Snapshot.Rows);
        _trendPoints = result.Snapshot.Trend;
        _timelinePoints = BuildTimeline(result.Timeline);
        HasAnyRecords = result.HasAnyRecords;
        _hasAnyKnown |= result.HasAnyRecords;
        _totalRows = result.Details.Total;
        IsSessionView = SessionOption?.Value != null;

        UpdateModelOptions(result.Models);
        UpdateSessionOptions(result.Sessions);
        BuildKpis();
        BuildFailures(result.Snapshot.Failures);
        BuildDetails(result.Details);
        RebuildRank();
        RebuildTrend();
        BuildSessionSummary(result.Timeline);
        UpdatePaging(result.Details.Total);

        ShowCacheWriteNote = _totals.Requests > 0 && !_totals.HasCachedWrite;
        UpdateEmptyState();
    }

    private void UpdatePaging(int total)
    {
        _totalRows = total;
        CanGoPrev = _pageIndex > 0;
        CanGoNext = _pageIndex + 1 < PageCount;
        PageInfo = total == 0
            ? Loc.Text(LangKey.UsagePageRowsEmpty)
            : Loc.Text(LangKey.UsagePageInfo, _pageIndex + 1, PageCount, total);
    }

    private void UpdateEmptyState()
    {
        IsEmpty = _totals.Requests == 0;
        if (!IsEmpty)
        {
            ShowGoToChat = false;
            EmptyTitle = "";
            EmptyHint = "";
            return;
        }

        ShowGoToChat = !HasAnyRecords;
        if (!HasAnyRecords)
        {
            EmptyTitle = Loc.Text(LangKey.UsageEmptyTitle);
            EmptyHint = Loc.Text(LangKey.UsageEmptyHint);
            return;
        }

        EmptyTitle = Loc.Text(LangKey.UsageEmptyFilteredTitle);
        EmptyHint = ModelOption?.Value is { } model
            ? Loc.Text(LangKey.UsageEmptyModelHint, model)
            : Loc.Text(LangKey.UsageEmptyFilteredHint);
    }

    private void UpdateModelOptions(IReadOnlyList<string> models)
    {
        string? selected = ModelOption?.Value;
        _suppressReload = true;
        try
        {
            ModelOptions.Clear();
            ModelOptions.Add(new UsageOptionViewData(null, Loc.Text(LangKey.UsageAllModels)));
            foreach (string model in models) ModelOptions.Add(new UsageOptionViewData(model, model));
            //钻取进来的模型可能不在当前范围里：补一项，别让选中值悬空
            if (selected != null && ModelOptions.All(x => !string.Equals(x.Value, selected, StringComparison.Ordinal)))
                ModelOptions.Add(new UsageOptionViewData(selected, selected));
            ModelOption = ModelOptions.FirstOrDefault(x => string.Equals(x.Value, selected, StringComparison.Ordinal))
                          ?? ModelOptions[0];
        }
        finally
        {
            _suppressReload = false;
        }
    }

    private void UpdateSessionOptions(IReadOnlyList<SessionUsageSummary> sessions)
    {
        string? selected = SessionOption?.Value;
        _suppressReload = true;
        try
        {
            SessionOptions.Clear();
            SessionOptions.Add(new UsageOptionViewData(null, Loc.Text(LangKey.UsageAllSessions)));
            foreach (SessionUsageSummary session in sessions)
            {
                SessionOptions.Add(new UsageOptionViewData(session.SessionId,
                    $"{UsageFormat.ShortSession(session.SessionId)} · {session.Requests} · {UsageFormat.Timestamp(session.LastTs)}"));
            }

            if (selected != null && SessionOptions.All(x => !string.Equals(x.Value, selected, StringComparison.Ordinal)))
                SessionOptions.Add(new UsageOptionViewData(selected, UsageFormat.ShortSession(selected)));
            SessionOption = SessionOptions.FirstOrDefault(x => string.Equals(x.Value, selected, StringComparison.Ordinal))
                            ?? SessionOptions[0];
        }
        finally
        {
            _suppressReload = false;
        }
    }

    private void UpdateSortHeaders()
    {
        TimeHeader = Header(LangKey.UsageColumnTime, EUsageDetailSortColumn.Time);
        ModelHeader = Header(LangKey.UsageColumnModel, EUsageDetailSortColumn.Model);
        InputHeader = Header(LangKey.UsageColumnInput, EUsageDetailSortColumn.InputTokens);
        OutputHeader = Header(LangKey.UsageColumnOutput, EUsageDetailSortColumn.OutputTokens);
        CacheHeader = Header(LangKey.UsageColumnCache, EUsageDetailSortColumn.CachedRead);
        LatencyHeader = Header(LangKey.UsageColumnLatency, EUsageDetailSortColumn.Latency);
        CostHeader = Header(LangKey.UsageColumnCost, EUsageDetailSortColumn.Cost);
    }

    private string Header(LangKey key, EUsageDetailSortColumn column)
    {
        string label = Loc.Text(key);
        if (_sortColumn != column) return label;
        return label + (_sortDirection == EUsageSortDirection.Descending ? " ↓" : " ↑");
    }

    private static void SelectOption(IReadOnlyCollection<UsageOptionViewData> options, string? value,
        Action<UsageOptionViewData> apply)
    {
        UsageOptionViewData? match = options.FirstOrDefault(x =>
            string.Equals(x.Value, value, StringComparison.Ordinal));
        if (match != null) apply(match);
    }
}
