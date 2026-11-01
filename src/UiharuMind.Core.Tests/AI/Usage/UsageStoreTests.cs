using System.Text;
using UiharuMind.Core.AI.Usage;

namespace UiharuMind.Core.Tests.AI.Usage;

/// <summary>
/// 数据层验收：写入、聚合、分页、时区分桶、会话时间线、失败分布、CSV。
/// 每个用例一份临时库，互不影响。
/// </summary>
public sealed class UsageStoreTests : IDisposable
{
    private readonly string _directory;
    private readonly UsageStore _store;

    public UsageStoreTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), $"uiharu-usage-{Guid.NewGuid():N}");
        _store = new UsageStore(Path.Combine(_directory, "Usage.db"));
    }

    public void Dispose()
    {
        _store.Dispose();
        try
        {
            Directory.Delete(_directory, true); //WAL 伴生文件一起删
        }
        catch (IOException)
        {
            //测试清理失败不影响结论
        }
    }

    [Fact]
    public void Append_ThenAggregate_KeepsPerModelRowsAndWeightedAverages()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "deepseek", "sensenova", 1000, 400, 200, latency: 100, firstToken: 20));
        _store.Insert(Record(now, "deepseek", "sensenova", 3000, 0, 600, latency: 300, firstToken: 60));
        _store.Insert(Record(now, "local-gguf", "", 500, 0, 100, isLocal: true, latency: 50));

        UsageSnapshot snapshot = _store.Query(new UsageQuery(Range(now)));

        Assert.Equal(2, snapshot.Rows.Count);
        UsageAggregateRow remote = snapshot.Rows.Single(x => !x.IsLocal);
        Assert.Equal(2, remote.Requests);
        Assert.Equal(4000, remote.InputTokens);
        Assert.Equal(400, remote.CachedRead);
        Assert.Equal(800, remote.OutputTokens);
        Assert.Equal(400, remote.LatencySumMs);
        Assert.Equal(80, remote.FirstTokenSumMs);
        Assert.Equal(2, remote.SuccessRequests);
        Assert.NotNull(remote.CostUsd);

        UsageTotals totals = UsageTotals.From(snapshot.Rows);
        Assert.Equal(3, totals.Requests);
        Assert.Equal(150, totals.AvgLatencyMs); //(100+300+50)/3，按请求加权而不是按行平均
        Assert.Equal(1, totals.LocalRequests);
        Assert.Equal(0, totals.ServiceFailures);
        Assert.True(totals.CacheHitRate is > 0.08 and < 0.1); //400 / 4500
        Assert.Equal(0, totals.UnpricedRequests); //两条远程都有价，本地那条不算「未定价」
    }

    [Fact]
    public void UnpricedRemoteRequests_AreCountedSeparatelyFromLocal()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "m", "p", 100, 0, 10, cost: null));
        _store.Insert(Record(now, "m", "p", 100, 0, 10, cost: 0.002m));
        _store.Insert(Record(now, "local", "", 100, 0, 10, isLocal: true));

        UsageTotals totals = UsageTotals.From(_store.Query(new UsageQuery(Range(now))).Rows);
        Assert.Equal(1, totals.UnpricedRequests);
        Assert.Equal(1, totals.LocalRequests);
        Assert.False(totals.OnlyLocal);
        Assert.Equal(0.003m, totals.CostUsd); //同组的和：0.001 + 0.002，未定价那条不当成 0 计
    }

    [Fact]
    public void Failures_AreSeparatedFromTokenAveragesAndCancelIsExcludedFromErrorRate()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "m", "p", 100, 0, 20, latency: 200));
        _store.Insert(Record(now, "m", "p", 0, 0, 0, ok: false, kind: UsageErrorKinds.Http,
            detail: "boom", status: 500));
        _store.Insert(Record(now, "m", "p", 0, 0, 0, ok: false, kind: UsageErrorKinds.Cancel));

        UsageSnapshot snapshot = _store.Query(new UsageQuery(Range(now)));
        UsageTotals totals = UsageTotals.From(snapshot.Rows);

        Assert.Equal(3, totals.Requests);
        Assert.Equal(2, totals.Failures);
        Assert.Equal(1, totals.ServiceFailures); //取消不算
        Assert.Equal(1.0 / 3, totals.ErrorRate!.Value, 3);
        Assert.Equal(200, totals.AvgLatencyMs); //失败行不进均值

        Assert.Equal(2, snapshot.Failures.Count);
        FailureKindStat http = snapshot.Failures.Single(x => x.ErrorKind == UsageErrorKinds.Http);
        Assert.Equal(1, http.Count);
        Assert.Equal(1, http.WithStatus);
        Assert.Equal(1, snapshot.Failures.Single(x => x.ErrorKind == UsageErrorKinds.Cancel).Count);
    }

    [Fact]
    public void Details_SortByWhitelistedColumnAndPageWithOffset()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        for (int i = 0; i < 5; i++)
            _store.Insert(Record(now - i * 1000, "m", "p", i * 100, 0, i * 10, latency: i * 5));

        UsageDetailPage first = _store.QueryDetails(new UsageDetailQuery(Range(now), Limit: 2, Offset: 0));
        Assert.Equal(5, first.Total);
        Assert.Equal(2, first.Rows.Count);
        Assert.Equal(0, first.Rows[0].InputTokens); //默认按时间倒序，最新的在前

        UsageDetailPage second = _store.QueryDetails(new UsageDetailQuery(Range(now), Limit: 2, Offset: 2));
        Assert.Equal(200, second.Rows[0].InputTokens);
        Assert.Equal(300, second.Rows[1].InputTokens);

        UsageDetailPage byInput = _store.QueryDetails(new UsageDetailQuery(Range(now),
            SortColumn: EUsageDetailSortColumn.InputTokens, SortDirection: EUsageSortDirection.Ascending,
            Limit: 10));
        Assert.Equal(0, byInput.Rows[0].InputTokens);
        Assert.Equal(400, byInput.Rows[^1].InputTokens);
    }

    [Fact]
    public void Details_OnlyFailedReturnsErrorFieldsOnly()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "m", "p", 100, 0, 10));
        _store.Insert(Record(now, "m", "p", 0, 0, 0, ok: false, kind: UsageErrorKinds.Timeout,
            detail: "read timed out", retry: 3));

        UsageDetailPage page = _store.QueryDetails(new UsageDetailQuery(Range(now), OnlyFailed: true));

        Assert.Equal(1, page.Total);
        UsageDetailRow row = page.Rows.Single();
        Assert.False(row.Ok);
        Assert.Equal(UsageErrorKinds.Timeout, row.ErrorKind);
        Assert.Equal("read timed out", row.ErrorDetail);
        Assert.Equal(3, row.RetryCount);
        Assert.Null(row.StatusCode);
    }

    [Fact]
    public void ModelAndSessionFilters_ApplyToAggregatesAndDetails()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "a", "p1", 100, 0, 10, session: "s1"));
        _store.Insert(Record(now, "b", "p2", 200, 0, 20, session: "s2"));

        UsageQuery modelQuery = new(Range(now), ModelFilter: "b");
        Assert.Equal("b", _store.Query(modelQuery).Rows.Single().ModelName);

        UsageQuery sessionQuery = new(Range(now), SessionFilter: "s1");
        Assert.Single(_store.Query(sessionQuery).Rows);
        Assert.Equal("a", _store.QueryDetails(new UsageDetailQuery(Range(now), SessionFilter: "s1")).Rows
            .Single().ModelName);
        Assert.Equal(["a", "b"], _store.KnownModelNames(Range(now).FromTs, Range(now).ToTs));
    }

    [Fact]
    public void Trend_BucketsByLocalDayWithoutTimezoneTricks()
    {
        //本地零点前后各一条：分桶必须按本地时区，而不是 UTC
        DateTimeOffset localMidnight = new(DateTime.Today.AddDays(-3), TimeZoneInfo.Local.GetUtcOffset(DateTime.Today));
        long dayStart = localMidnight.ToUnixTimeMilliseconds();
        long inDayOne = dayStart + 3_600_000;
        long inDayTwo = dayStart + 86_400_000 + 3_600_000;
        _store.Insert(Record(inDayOne, "m", "p", 100, 0, 10));
        _store.Insert(Record(inDayTwo, "m", "p", 200, 0, 20));

        UsageRange range = new(dayStart, dayStart + 2 * 86_400_000, EUsageGranularity.Day);
        UsageSnapshot snapshot = _store.Query(new UsageQuery(range));

        Assert.Equal(2, snapshot.Trend.Count);
        Assert.Equal(dayStart, snapshot.Trend[0].BucketTs);
        Assert.Equal(110, snapshot.Trend[0].Tokens);
        Assert.Equal(dayStart + 86_400_000, snapshot.Trend[1].BucketTs);

        //补空桶：中间空一天要落在 0 上
        UsageRange wider = new(dayStart, dayStart + 3 * 86_400_000, EUsageGranularity.Day);
        IReadOnlyList<long> buckets = UsageTrendFill.Buckets(wider, TimeZoneInfo.Local);
        IReadOnlyList<UsageTrendPoint> filled = UsageTrendFill.Fill(snapshot.Trend, buckets);
        Assert.Equal(3, filled.Count);
        Assert.Equal(0, filled[2].Tokens);
        Assert.Equal(2, filled.Count(x => x.Requests > 0));
    }

    [Fact]
    public void WeekBucket_StartsOnLocalMonday()
    {
        //任意一个周三中午：它所在的周桶应当是那个周一的本地零点
        DateTime wednesday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7) + 2).AddHours(12);
        DateTimeOffset start = new(wednesday, TimeZoneInfo.Local.GetUtcOffset(wednesday));
        UsageRange range = new(start.ToUnixTimeMilliseconds() - 10 * 86_400_000,
            start.ToUnixTimeMilliseconds() + 86_400_000, EUsageGranularity.Week);

        long anchor = UsageBucket.AnchorFor(range, TimeZoneInfo.Local);
        DateTimeOffset anchored = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(anchor),
            TimeZoneInfo.Local);
        Assert.Equal(DayOfWeek.Monday, anchored.DayOfWeek);
        Assert.Equal(TimeSpan.Zero, anchored.TimeOfDay);

        //目标时刻落在哪个桶：必须是它自己那一周的周一，且不超过它
        long bucket = UsageBucket.Of(start.ToUnixTimeMilliseconds(), anchor, EUsageGranularity.Week);
        DateTimeOffset bucketStart = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(bucket),
            TimeZoneInfo.Local);
        Assert.Equal(DayOfWeek.Monday, bucketStart.DayOfWeek);
        Assert.Equal(TimeSpan.Zero, bucketStart.TimeOfDay);
        Assert.InRange(bucket, start.AddDays(-7).ToUnixTimeMilliseconds(),
            start.ToUnixTimeMilliseconds());
    }

    [Fact]
    public void SessionTimelineAndSessionList_ReadBackTheSession()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now - 2000, "m", "p", 100, 80, 10, session: "s1"));
        _store.Insert(Record(now - 1000, "m", "p", 100, 0, 10, session: "s1"));
        _store.Insert(Record(now, "m", "p", 100, 0, 10, session: "s2"));

        IReadOnlyList<UsageDetailRow> timeline = _store.QuerySessionTimeline("s1",
            now - 10_000, now + 10_000);
        Assert.Equal(2, timeline.Count);
        Assert.True(timeline[0].Ts <= timeline[1].Ts); //按时间升序
        Assert.Equal(80, timeline[0].CachedRead);

        IReadOnlyList<SessionUsageSummary> sessions = _store.ListSessions(now - 10_000, now + 10_000);
        Assert.Equal(2, sessions.Count);
        Assert.Equal("s2", sessions[0].SessionId); //最后活动的在前
        Assert.Equal(2, sessions.Single(x => x.SessionId == "s1").Requests);
    }

    [Fact]
    public void ExportCsv_WritesWholeFilteredSetWithBom()
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        _store.Insert(Record(now, "m", "p", 100, 0, 10, session: "s1"));
        _store.Insert(Record(now, "m", "p", 0, 0, 0, ok: false, kind: UsageErrorKinds.Http,
            detail: "含,逗号 与\"引号\"", status: 502));
        _store.Insert(Record(now, "other", "p", 100, 0, 10));

        string path = Path.Combine(_directory, "export.csv");
        long rows = _store.ExportCsv(new UsageDetailQuery(Range(now), ModelFilter: "m"), path);

        Assert.Equal(2, rows); //导出的是筛选后的全集，不是当前页
        byte[] bytes = File.ReadAllBytes(path);
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes.Take(3).ToArray()); //BOM：Excel 认它才不乱码

        string text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        string[] lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length); //表头 + 两行
        Assert.Contains("含,逗号", text);
        Assert.Contains("\"含,逗号 与\"\"引号\"\"\"", text); //含逗号/引号的字段必须被包起来
        Assert.DoesNotContain("other", text);
    }

    [Fact]
    public void Journal_AppendsWithoutBlockingAndFlushesOnShutdown()
    {
        string path = Path.Combine(_directory, "journal", "Usage.db");
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        using (UsageJournal journal = new(path))
        {
            journal.Append(Record(now, "m", "p", 100, 0, 10));
            journal.Append(Record(now, "m", "p", 100, 0, 10));
        }

        using UsageStore store = new(path);
        Assert.Equal(2, store.QueryDetails(new UsageDetailQuery(Range(now))).Total);
    }

    private static UsageRange Range(long now) =>
        new(now - 86_400_000, now + 86_400_000, EUsageGranularity.Day);

    private static UsageRecord Record(long ts, string model, string providerKey, long input, long cached, long output,
        string session = "s", bool isLocal = false, int? latency = null, int? firstToken = null, bool ok = true,
        string? kind = null, string? detail = null, int? status = null, int retry = 0, decimal? cost = 0.001m) => new(
        ts, ts - 100, session, model, model, isLocal ? "local" : "RemoteTestConfig", providerKey, "", isLocal,
        input, cached, null, output, 0, ok ? latency : null, ok ? firstToken : null, ok, kind, detail, status, retry,
        ok ? cost : null, ok && cost != null ? UsagePricing.SourceOfficial : null, ok && cost != null ? 0.15m : null,
        ok && cost != null ? 0.6m : null, ok && cost != null ? 0.015m : null, 1m);
}
