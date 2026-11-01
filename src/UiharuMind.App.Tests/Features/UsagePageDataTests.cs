using Avalonia.Threading;
using UiharuMind.App.Tests.Headless;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Features.Models.Usage;

namespace UiharuMind.App.Tests.Features;

/// <summary>
/// 用量页状态宿主的逻辑验收：筛选、聚合、分页、会话视图、空态。
/// <b>不实例化任何图表控件</b>（设计文档 7.4）——series 只是对象，构造它们不需要 Skia 渲染
/// </summary>
[Collection(HeadlessCollection.Name)]
public sealed class UsagePageDataTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"uiharu-usage-page-{Guid.NewGuid():N}");
    private readonly List<IDisposable> _disposables = [];

    public void Dispose()
    {
        foreach (IDisposable disposable in _disposables) disposable.Dispose();
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
            //测试清理失败不影响结论
        }
    }

    [Fact]
    public void Load_BuildsKpisRankDetailsAndFailures()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            Seed(path, SeedThreeRows);
            UsagePageData data = NewPage(path);
            data.OnEnable();

            PumpUntil(() => !data.IsLoading && data.Kpis.Count > 0);

            Assert.Equal(6, data.Kpis.Count); //六张卡：token / 请求 / 命中 / 费用 / 首token / 错误率
            Assert.False(data.IsEmpty);
            Assert.Equal(3, data.Details.Count);
            Assert.NotEmpty(data.RankItems);
            Assert.NotEmpty(data.Failures);
            Assert.Contains("1", data.PageInfo); //「第 1 / 1 页 · 共 3 行」
            Assert.Equal(2, data.TrendSeries.Length); //默认双轴：token 柱 + 费用线
            Assert.All(data.Details, row => Assert.NotEmpty(row.TimeText));
        });
    }

    [Fact]
    public void Paging_ShowsFiftyPerPageAndDoesNotRepeatRows()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Seed(path, store =>
            {
                for (int i = 0; i < 60; i++)
                    store.Insert(Row(now - i * 1000, "model-a", "s1", input: 100 + i));
            });

            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            Assert.Equal(50, data.Details.Count);
            Assert.True(data.CanGoNext);
            Assert.False(data.CanGoPrev);
            long[] firstPage = [.. data.Details.Select(x => x.Id)];

            data.NextPageCommand.Execute(null);
            PumpUntil(() => !data.IsLoadingDetails && data.Details.Count == 10);

            Assert.Equal(10, data.Details.Count);
            Assert.DoesNotContain(data.Details, x => firstPage.Contains(x.Id));
            Assert.True(data.CanGoPrev);
            Assert.False(data.CanGoNext);
        });
    }

    [Fact]
    public void OnlyFailed_FiltersDetailsToFailures()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            Seed(path, SeedThreeRows);
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            data.OnlyFailed = true;
            PumpUntil(() => !data.IsLoading && data.Details.Count == 1);

            UsageDetailItemViewData row = Assert.Single(data.Details);
            Assert.False(row.Ok);
            Assert.True(row.HasFailedDetail);
        });
    }

    [Fact]
    public void ModelFilter_AppliesToDetailsAndKeepsSelection()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            Seed(path, SeedThreeRows);
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            UsageOptionViewData model = data.ModelOptions.Single(x => x.Value == "model-a");
            data.ModelOption = model;
            PumpUntil(() => !data.IsLoading && data.Details.Count == 2);

            Assert.All(data.Details, row => Assert.Equal("model-a", row.ModelName));
            Assert.Same(model, data.ModelOption);
        });
    }

    [Fact]
    public void SessionDrill_SwitchesTrendToHitRateTimelineAndMarksDrop()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            //同一会话：前两发全命中，第三发骤降到 0 —— 缓存失效点
            Seed(path, store =>
            {
                store.Insert(Row(now - 3000, "model-a", "session-x", input: 1000, cached: 1000));
                store.Insert(Row(now - 2000, "model-a", "session-x", input: 1000, cached: 1000));
                store.Insert(Row(now - 1000, "model-a", "session-x", input: 1000, cached: 0));
                store.Insert(Row(now, "model-b", "session-y", input: 500));
            });

            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            data.DrillSessionCommand.Execute("session-x");
            PumpUntil(() => !data.IsLoading && data.IsSessionView && data.SessionSummary != null);

            Assert.True(data.IsSessionView);
            Assert.Equal("session-x", data.SessionSummary!.SessionId);
            Assert.Contains("3", data.SessionSummary.SummaryText);
            Assert.Contains("3", data.SessionSummary.DropHint); //第 3 条请求后骤降
            Assert.Equal(2, data.TrendSeries.Length); //命中率线 + 骤降点标记
            Assert.All(data.Details, row => Assert.Equal("session-x", row.SessionId));

            data.ClearSessionCommand.Execute(null);
            PumpUntil(() => !data.IsLoading && !data.IsSessionView);
            Assert.Null(data.SessionSummary);
        });
    }

    [Fact]
    public void SortByHeader_OrdersRowsAndTogglesDirection()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            Seed(path, store =>
            {
                store.Insert(Row(now - 3000, "model-a", "s1", input: 300));
                store.Insert(Row(now - 2000, "model-a", "s1", input: 100));
                store.Insert(Row(now - 1000, "model-a", "s1", input: 200));
            });
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count == 3);

            //数值列默认正序（小的在前）
            data.SortByCommand.Execute("InputTokens");
            PumpUntil(() => !data.IsLoadingDetails && data.Details[0].InputText == "100");
            Assert.Equal(["100", "200", "300"], data.Details.Select(x => x.InputText));
            Assert.Contains("↑", data.InputHeader);

            //再点一次同一列 -> 倒序
            data.SortByCommand.Execute("InputTokens");
            PumpUntil(() => !data.IsLoadingDetails && data.Details[0].InputText == "300");
            Assert.Equal(["300", "200", "100"], data.Details.Select(x => x.InputText));
            Assert.Contains("↓", data.InputHeader);
        });
    }

    [Fact]
    public void NoRecords_ShowsGuidingEmptyStateWithGoToChat()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading);

            Assert.True(data.IsEmpty);
            Assert.True(data.ShowGoToChat);
            Assert.NotEmpty(data.EmptyTitle);
            Assert.Empty(data.Details);
            //空态下 KPI 仍有卡（值是「—」/「未公布」），但整块由 IsEmpty 隐藏，不闪布局
            Assert.All(data.Kpis, card => Assert.True(card.Value is "—" or "未公布"));
        });
    }

    [Fact]
    public void FilteredEmpty_DescribesTheModelInsteadOfGoToChat()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            Seed(path, SeedThreeRows);
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            //挑一个在当前范围里没有记录的模型：空态文案点明模型名，但不给「去对话」
            data.ModelOption = new UsageOptionViewData("no-such-model", "no-such-model");
            PumpUntil(() => !data.IsLoading && data.IsEmpty);

            Assert.False(data.ShowGoToChat);
            Assert.Contains("no-such-model", data.EmptyHint);
        });
    }

    [Fact]
    public void Disable_ReleasesHeavyDataButKeepsFilters()
    {
        HeadlessUi.Run(() =>
        {
            string path = NewPath();
            Seed(path, SeedThreeRows);
            UsagePageData data = NewPage(path);
            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);

            data.RangeOption = data.RangeOptions[0]; //今日
            PumpUntil(() => !data.IsLoading);

            data.OnDisable();
            Assert.Empty(data.Details);
            Assert.Empty(data.Kpis);
            Assert.Empty(data.RankItems);
            Assert.Empty(data.TrendSeries);
            Assert.Same(data.RangeOptions[0], data.RangeOption); //筛选状态留在实例上

            data.OnEnable();
            PumpUntil(() => !data.IsLoading && data.Details.Count > 0);
            Assert.Same(data.RangeOptions[0], data.RangeOption);
        });
    }

    private string NewPath() => Path.Combine(_directory, Guid.NewGuid().ToString("N"), "Usage.db");

    private static void Seed(string path, Action<UsageStore> fill)
    {
        using UsageStore store = new(path);
        fill(store);
    }

    private UsagePageData NewPage(string path)
    {
        UsageJournal journal = new(path);
        _disposables.Add(journal);
        return new UsagePageData(null, journal, null);
    }

    private static void SeedThreeRows(UsageStore store)
    {
        long now = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        store.Insert(Row(now - 2000, "model-a", "session-x", input: 1000, cached: 800, output: 200, latency: 1200));
        store.Insert(Row(now - 1000, "model-a", "session-x", input: 500, output: 100, latency: 900));
        store.Insert(Row(now, "model-b", "session-y", input: 300, output: 60, isLocal: true, latency: 300,
            ok: false, kind: UsageErrorKinds.Http, status: 500));
    }

    private static UsageRecord Row(long ts, string model, string session, long input = 100, long cached = 0,
        long output = 20, bool isLocal = false, int? latency = 500, bool ok = true, string? kind = null,
        int? status = null) => new(
        ts, ts - 100, session, model, model, isLocal ? "local" : "RemoteTestConfig", isLocal ? "" : "test",
        "https://example.com", isLocal, input, cached, null, output, 0, ok ? latency : null, null, ok, kind,
        ok ? null : "boom", status, 0,
        isLocal ? null : ok ? 0.001m : null, isLocal ? null : ok ? UsagePricing.SourceOfficial : null,
        isLocal ? null : 0.15m, isLocal ? null : 0.6m, isLocal ? null : 0.015m, 1m);

    private static void PumpUntil(Func<bool> condition, int timeoutMs = 8000)
    {
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }

        Dispatcher.UIThread.RunJobs();
        Assert.True(condition(), "用量页的异步加载没有在超时前完成");
    }
}
