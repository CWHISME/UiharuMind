using UiharuMind.Core.AI.Status;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// 窗口阈值口径：打满优先于接近上限，0.9 是含值边界。
/// </summary>
public class ProviderStatusRulesTests
{
    private static ProviderUsageMeter Meter(double used, double cap) =>
        new(EProviderMeterSlot.FiveHour, used, cap, "$", null, false);

    [Theory]
    [InlineData(8.9, 0)]
    [InlineData(9.0, 1)]
    [InlineData(9.99, 1)]
    [InlineData(10, 1)]
    public void NearLimit_StartsAtNinetyPercent(double used, int expectedCount)
    {
        IReadOnlyList<ProviderStatusNotice> notices = ProviderStatusRules.MeterNotices([Meter(used, 10)]);

        Assert.Equal(expectedCount, notices.Count);
    }

    [Fact]
    public void Exhausted_BeatsNearLimit()
    {
        IReadOnlyList<ProviderStatusNotice> notices = ProviderStatusRules.MeterNotices([Meter(10, 10)]);

        Assert.Equal(EProviderNoticeKind.WindowExhausted, Assert.Single(notices).Kind);
    }

    [Fact]
    public void ExhaustedFlag_WithoutFullUsage_StillExhausted()
    {
        ProviderUsageMeter meter = new(EProviderMeterSlot.Weekly, 1, 10, "$", null, true);

        Assert.True(meter.IsExhausted);
        Assert.Equal(EProviderNoticeKind.WindowExhausted, Assert.Single(ProviderStatusRules.MeterNotices([meter])).Kind);
    }

    [Fact]
    public void Tightest_PrefersExhaustedThenHighestRatio()
    {
        ProviderUsageMeter fiveHour = Meter(3, 10);
        ProviderUsageMeter weekly = new(EProviderMeterSlot.Weekly, 92, 100, "$", null, false);

        Assert.Equal(EProviderMeterSlot.Weekly, ProviderStatusRules.Tightest([fiveHour, weekly])!.Slot);
        Assert.Null(ProviderStatusRules.Tightest([]));
    }

    [Fact]
    public void NoMeters_NoNotices()
    {
        Assert.Empty(ProviderStatusRules.MeterNotices([]));
    }
}
