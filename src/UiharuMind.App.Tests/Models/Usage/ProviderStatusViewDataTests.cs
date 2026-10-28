using System;
using System.Threading;
using System.Threading.Tasks;
using UiharuMind.App.Tests.TestDoubles;
using UiharuMind.Core.AI.Status;
using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Features.Models.Usage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.Models.Usage;

/// <summary>
/// 顶栏读数的文案与状态档：正常不出点、接近上限转黄、打满/凭据失效转红、高峰挂徽章。
/// 断言用 <see cref="Loc.Text"/> 拼期望值，跟着语言走，不写死中文。
///
/// 这不是无头测试：读数类是纯视图数据，定时拍由外面调 <c>Tick()</c> 驱动，
/// 不需要控件树，也就不需要 <c>HeadlessUi</c>。
/// </summary>
public sealed class ProviderStatusViewDataTests
{
    private static readonly Type CcType = typeof(RemoteCommandCodeModelConfig);
    private static readonly DateTimeOffset MondayTwoUtc = new(2026, 10, 26, 2, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset NoonUtc = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoModel_IsEmpty()
    {
        Harness harness = new(() => null);
        harness.Data.Pump();

        Assert.True(harness.Data.IsEmpty);
        Assert.Equal(0, harness.Source.Calls);
    }

    [Fact]
    public void UnregisteredProvider_IsEmptyAndNeverFetches()
    {
        Harness harness = new(() => new ProviderStatusContext(typeof(object), "https://example.com", "k", "m", "m"));
        harness.Data.Pump();

        Assert.True(harness.Data.IsEmpty);
        Assert.Equal(0, harness.Source.Calls);
    }

    [Fact]
    public void NormalUsage_ShowsBothWindowsWithoutDot()
    {
        Harness harness = new(() => Context(), now => Snapshot(Meter(3.2, 10), Meter(41, 100)));
        harness.Data.Pump();

        Assert.False(harness.Data.IsEmpty);
        Assert.Contains("32%", harness.Data.Readout);
        Assert.Contains("41%", harness.Data.Readout);
        Assert.False(harness.Data.HasDot); //常态不出点：顶栏已经有一个本机占用的点了
        Assert.False(harness.Data.HasBadge);
    }

    [Fact]
    public void NearLimit_TurnsWarning()
    {
        Harness harness = new(() => Context(), now => Snapshot(Meter(9.2, 10), Meter(41, 100)));
        harness.Data.Pump();

        Assert.True(harness.Data.HasDot);
        Assert.Equal("Warning", harness.Data.DotTag);
    }

    [Fact]
    public void Exhausted_TurnsErrorAndSaysWhenItResets()
    {
        Harness harness = new(() => Context(), now => Snapshot(Meter(10, 10, now.AddMinutes(12))));
        harness.Data.Pump();

        Assert.True(harness.Data.HasDot);
        Assert.Equal("Error", harness.Data.DotTag);
        Assert.Contains("100%", harness.Data.Readout);
        Assert.Contains(
            Loc.Text(LangKey.ProviderStatusResetsIn, Loc.Text(LangKey.ProviderStatusSpanMinutes, 12)),
            harness.Data.Readout);
    }

    [Fact]
    public void PeakModel_GetsPeakBadge()
    {
        Harness harness = new(() => Context("deepseek-v4.1-flash"), now => Snapshot(Meter(3, 10)),
            MondayTwoUtc.AddMinutes(30)); //UTC 02:30 在高峰窗口内，距窗口结束 1 时 30 分
        harness.Data.Pump();

        Assert.True(harness.Data.HasBadge);
        Assert.Contains("×2", harness.Data.BadgeText);
        Assert.Contains(Loc.Text(LangKey.ProviderStatusSpanHours, 1, 30), harness.Data.BadgeText);
    }

    [Fact]
    public void OffPeakModel_HasNoBadge()
    {
        Harness harness = new(() => Context("kimi-k3"), now => Snapshot(Meter(3, 10)), MondayTwoUtc);
        harness.Data.Pump();

        Assert.False(harness.Data.HasBadge);
    }

    [Fact]
    public void FreeAccountWithoutWindows_ShowsBalanceInsteadOfBlank()
    {
        // 实测的免费账户：没有滚动窗口，余额三个桶都是 0。读数退到余额，
        // 而不是留一片空白让用户以为坏了
        Harness harness = new(() => Context(),
            _ => new ProviderStatusSnapshot("Command Code", [], new ProviderBalance(0, "$", null), [],
                DateTimeOffset.Now));
        harness.Data.Pump();

        Assert.False(harness.Data.IsEmpty);
        Assert.False(harness.Data.HasMeters);
        Assert.True(harness.Data.HasBalance);
        Assert.Equal(Loc.Text(LangKey.ProviderStatusBalance, "$", "0"), harness.Data.Readout);
    }

    [Fact]
    public void CredentialInvalid_ReplacesReadoutAndTurnsRed()
    {
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [], null,
            [new ProviderStatusNotice(EProviderNoticeKind.CredentialInvalid)], DateTimeOffset.Now));
        harness.Data.Pump();

        Assert.False(harness.Data.IsEmpty);
        Assert.True(harness.Data.HasCredentialWarning);
        Assert.Equal(Loc.Text(LangKey.ProviderStatusCredentialInvalid), harness.Data.Readout);
        Assert.Equal("Error", harness.Data.DotTag);
    }

    [Fact]
    public void SwitchingToUnregisteredProvider_ClearsPreviousNumbers()
    {
        ProviderStatusContext? current = Context();
        Harness harness = new(() => current, _ => Snapshot(Meter(3, 10)));
        harness.Data.Pump();
        Assert.False(harness.Data.IsEmpty);

        current = new ProviderStatusContext(typeof(object), "https://example.com", "k", "m", "m");
        harness.Data.Pump();

        Assert.True(harness.Data.IsEmpty);
        Assert.Equal("", harness.Data.Readout);
    }

    [Fact]
    public async Task TurnEnded_RespectsTtl()
    {
        Harness harness = new(() => Context(), _ => Snapshot(Meter(3, 10)));
        harness.Data.Pump();
        Assert.Equal(1, harness.Source.Calls);

        harness.Data.NotifyTurnEnded();
        await harness.Data.RefreshAsync();
        Assert.Equal(1, harness.Source.Calls); //60 秒内不重复请求

        harness.Clock.Advance(TimeSpan.FromSeconds(61));
        await harness.Data.RefreshAsync();
        Assert.Equal(2, harness.Source.Calls);
    }

    [Fact]
    public void BalanceBuckets_BecomePanelRows()
    {
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(69.97, "$", null,
            [
                new ProviderCreditBucket(EProviderCreditBucket.Monthly, 69.97),
                new ProviderCreditBucket(EProviderCreditBucket.Purchased, 0),
                new ProviderCreditBucket(EProviderCreditBucket.Free, 0)
            ]), [], DateTimeOffset.Now));
        harness.Data.Pump();

        // 三个桶只有月度有钱：只列那一行，0 的桶不上面板
        ProviderStatusBucketRow row = Assert.Single(harness.Data.BucketRows);
        Assert.True(harness.Data.HasBuckets);
        Assert.Equal(Loc.Text(LangKey.ProviderStatusBucketMonthly), row.Label);
        Assert.Equal("$69.97", row.AmountText);
    }

    [Fact]
    public void AllZeroBuckets_ShowNoRows()
    {
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(0, "$", null, [new ProviderCreditBucket(EProviderCreditBucket.Monthly, 0)]), [],
            DateTimeOffset.Now));
        harness.Data.Pump();

        // 「余额 $0」已经说完了，再挂一行 $0 只是噪音
        Assert.False(harness.Data.HasBuckets);
        Assert.Empty(harness.Data.BucketRows);
    }

    [Fact]
    public void PanelTitle_CarriesTheAccountName()
    {
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(69.93, "$", null, [new ProviderCreditBucket(EProviderCreditBucket.Monthly, 69.93)]),
            [], NoonUtc, "JiaYingWang"));
        harness.Data.Pump();

        Assert.Contains("Command Code", harness.Data.PanelTitle);
        Assert.Contains("JiaYingWang", harness.Data.PanelTitle);
    }

    [Fact]
    public async Task MonthlyProgress_DenominatorIsRemainingPlusUsed()
    {
        DateTimeOffset periodStart = new(2026, 10, 28, 10, 40, 16, TimeSpan.Zero);
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(69.93, "$", null, [new ProviderCreditBucket(EProviderCreditBucket.Monthly, 69.93)]),
            [], NoonUtc), NoonUtc);
        harness.Source.SummaryFactory = _ => new ProviderUsageSummary(0.07, "$", periodStart, 43, 100);

        harness.Data.Pump();
        await harness.Data.RefreshSummaryAsync();

        // 接口不给「本月上限」，分母是剩余 + 本周期已用推的
        Assert.True(harness.Data.HasMonthlyProgress);
        Assert.Equal(0.07 / 70.0, harness.Data.MonthlyRatio, 1e-6);
        Assert.Equal(Loc.Text(LangKey.ProviderStatusMonthlyUsed, "$0.07", "$70"),
            harness.Data.MonthlyAmountText);
        // 日期与倒计时都要（用户拍板）
        DateTimeOffset resetAt = periodStart.AddMonths(1);
        Assert.Equal(
            Loc.Text(LangKey.ProviderStatusMonthlyResetLine, resetAt.ToLocalTime().ToString("yyyy-MM-dd"),
                Loc.Text(LangKey.ProviderStatusSpanDays, 32, 22)), harness.Data.MonthlyResetText);
        Assert.True(harness.Data.HasUsageStats);
    }

    [Fact]
    public async Task MonthlyProgress_HidesTheDuplicateBucketRow()
    {
        // 进度块已经叫「本月额度」，桶行再写一次同名只是重复（用户截图指出）
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(69.93, "$", null, [new ProviderCreditBucket(EProviderCreditBucket.Monthly, 69.93)]),
            [], NoonUtc), NoonUtc);
        harness.Source.SummaryFactory = _ =>
            new ProviderUsageSummary(0.07, "$", new DateTimeOffset(2026, 10, 28, 10, 40, 16, TimeSpan.Zero), 43, 100);

        harness.Data.Pump();
        await harness.Data.RefreshSummaryAsync();

        Assert.True(harness.Data.HasMonthlyProgress);
        Assert.False(harness.Data.HasBuckets);
        Assert.Empty(harness.Data.BucketRows);
    }

    [Fact]
    public async Task MonthlyProgress_UntrustworthyDenominator_ShowsNoBar()
    {
        // 剩余为负 → 分母站不住：宁可少一条，不可给错的分母
        Harness harness = new(() => Context(), _ => new ProviderStatusSnapshot("Command Code", [],
            new ProviderBalance(-1, "$", null, [new ProviderCreditBucket(EProviderCreditBucket.Monthly, -1)]),
            [], NoonUtc), NoonUtc);
        harness.Source.SummaryFactory = _ =>
            new ProviderUsageSummary(5, "$", new DateTimeOffset(2026, 10, 28, 0, 0, 0, TimeSpan.Zero), 3, 100);

        harness.Data.Pump();
        await harness.Data.RefreshSummaryAsync();

        Assert.False(harness.Data.HasMonthlyProgress);
        Assert.Empty(harness.Data.MonthlyPercentText);
    }

    [Fact]
    public void SmallPercent_KeepsOneDecimal()
    {
        // 0.5% 四舍五入成 1% 看着像翻倍，小数值保留一位
        Harness harness = new(() => Context(), _ => Snapshot(Meter(0.5, 100)));
        harness.Data.Pump();

        Assert.Contains("0.5%", harness.Data.Readout);
    }

    [Fact]
    public void MeterRows_AreBuiltForPanel()
    {
        Harness harness = new(() => Context(),
            now => Snapshot(Meter(3.2, 10, now.AddHours(3)), Meter(41, 100)));
        harness.Data.Pump();

        Assert.True(harness.Data.HasMeters);
        Assert.Equal(2, harness.Data.MeterRows.Count);
        Assert.Equal("32%", harness.Data.MeterRows[0].PercentText);
        Assert.True(harness.Data.MeterRows[0].HasReset);
        Assert.False(harness.Data.MeterRows[1].HasReset);
    }

    [Fact]
    public async Task TwoSurfaces_SameProvider_ShareOneRequest()
    {
        // 两个界面挂着同一家供应商（同一把 key）：只发一次请求。
        // 定时拍归公共调度器管，HTTP 归服务层按 key 缓存管——两层都不按界面数翻倍
        DateTimeOffset now = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
        StubProviderStatusSource source = new() { Clock = () => now };
        source.SnapshotFactory = _ => new ProviderStatusSnapshot("Command Code",
            [new ProviderUsageMeter(EProviderMeterSlot.FiveHour, 3, 10, "$", null, false)], null, [], now);
        ProviderStatusService status = new(new StubProviderStatusRegistry(CcType, source), () => now);

        ProviderStatusViewData first = new(() => Context(), status, () => now, action => action());
        ProviderStatusViewData second = new(() => Context(), status, () => now, action => action());

        await first.RefreshAsync();
        await second.RefreshAsync();

        Assert.Equal(1, source.Calls);
        Assert.False(first.IsEmpty);
        Assert.False(second.IsEmpty);
        Assert.Equal(first.Readout, second.Readout);

        first.Dispose();
        second.Dispose();
    }

    [Fact]
    public void RefreshAge_CountsSeconds()
    {
        // 「数据 47 秒前」得自己往上爬，而不是等下一轮 30 秒的拍子
        Harness harness = new(() => Context(), _ => Snapshot(Meter(3, 10)));
        harness.Data.Pump();
        string before = harness.Data.DataAgeText;

        harness.Clock.Advance(TimeSpan.FromSeconds(5));
        harness.Data.RefreshAge();

        Assert.NotEqual(before, harness.Data.DataAgeText);
        Assert.Contains(Loc.Text(LangKey.ProviderStatusAgeSeconds, 5), harness.Data.DataAgeText);
    }

    [Fact]
    public void BeginRefresh_ShowsRefreshingInsteadOfTheOldAge()
    {
        Harness harness = new(() => Context(), _ => Snapshot(Meter(3, 10)));
        harness.Data.Pump();
        harness.Clock.Advance(TimeSpan.FromSeconds(47));

        // 点开明细：先说「更新中…」，别把上一轮的 47 秒闪给用户看
        harness.Data.BeginRefresh();
        Assert.Equal(Loc.Text(LangKey.ProviderStatusRefreshing), harness.Data.DataAgeText);

        // 秒级跳动也不能把「更新中」顶掉
        harness.Data.RefreshAge();
        Assert.Equal(Loc.Text(LangKey.ProviderStatusRefreshing), harness.Data.DataAgeText);
    }

    [Fact]
    public async Task ForcedRefresh_EndsWithJustNow()
    {
        Harness harness = new(() => Context(), _ => Snapshot(Meter(3, 10)));
        harness.Data.Pump();
        harness.Clock.Advance(TimeSpan.FromSeconds(47));

        await harness.Data.RefreshAsync(force: true);

        // 新数据到手后不说「0秒前」而说「刚刚」
        Assert.False(harness.Data.IsRefreshing);
        Assert.Equal(Loc.Text(LangKey.ProviderStatusDataJustNow), harness.Data.DataAgeText);
    }

    //================= 造数据 =================

    private static ProviderStatusContext Context(string modelId = "deepseek-v4-flash") =>
        new(CcType, "https://api.commandcode.ai", "key-a", modelId, modelId);

    private static ProviderUsageMeter Meter(double used, double cap, DateTimeOffset? resetAt = null) =>
        new(EProviderMeterSlot.FiveHour, used, cap, "$", resetAt, false);

    private static ProviderStatusSnapshot Snapshot(params ProviderUsageMeter[] meters) =>
        new("Command Code", meters, null, [], DateTimeOffset.Now);

    /// <summary>一份读数 + 它的假数据源（注册表换成只认 CC 的那张，不外连）</summary>
    private sealed class Harness
    {
        public Harness(Func<ProviderStatusContext?> context,
            Func<DateTimeOffset, ProviderStatusSnapshot?>? snapshot = null, DateTimeOffset? now = null)
        {
            Clock = new TestClock(now ?? NoonUtc);
            Source.SnapshotFactory = snapshot;
            Source.Clock = Clock.Read;
            ProviderStatusService status = new(new StubProviderStatusRegistry(CcType, Source), Clock.Read);
            Data = new ProviderStatusViewData(context, status, Clock.Read, action => action());
        }

        public TestClock Clock { get; }

        public StubProviderStatusSource Source { get; } = new();

        public ProviderStatusViewData Data { get; }
    }
}
