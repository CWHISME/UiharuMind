using UiharuMind.Core.AI.Status;
using UiharuMind.Core.AI.Status.Providers.CommandCode;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// CC 的高峰/免费/促销判定：UTC 边界的左闭右开、周末不算、非 DeepSeek 无时段概念。
/// 边界值一律用 UTC 时刻写，避免测试跟着开发机时区漂。
/// </summary>
public class CommandCodeBillingCalendarTests
{
    private readonly CommandCodeBillingCalendar _calendar = new();

    private static ProviderStatusContext Context(string modelId) =>
        new(typeof(RemoteCommandCodeModelConfig), "https://api.commandcode.ai", "k", modelId, modelId);

    // 2026-10-26 是周一
    private static DateTimeOffset Utc(int hour, int minute = 0, int day = 26) =>
        new(2026, 10, day, hour, minute, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0, 59, false)]
    [InlineData(1, 0, true)]
    [InlineData(3, 59, true)]
    [InlineData(4, 0, false)]
    [InlineData(5, 59, false)]
    [InlineData(6, 0, true)]
    [InlineData(9, 59, true)]
    [InlineData(10, 0, false)]
    public void PeakWindow_IsHalfOpen(int hour, int minute, bool expectedPeak)
    {
        DateTimeOffset? until = CommandCodeBillingCalendar.PeakUntil(Utc(hour, minute));

        Assert.Equal(expectedPeak, until != null);
    }

    [Fact]
    public void PeakUntil_IsTheEndOfTheCurrentSegment()
    {
        Assert.Equal(Utc(4), CommandCodeBillingCalendar.PeakUntil(Utc(2, 30)));
        Assert.Equal(Utc(10), CommandCodeBillingCalendar.PeakUntil(Utc(7)));
    }

    [Theory]
    [InlineData(24)] // 周六
    [InlineData(25)] // 周日
    public void Weekend_IsNeverPeak(int day)
    {
        Assert.Null(CommandCodeBillingCalendar.PeakUntil(Utc(2, 30, day)));
    }

    [Fact]
    public void LocalTime_IsConvertedBeforeJudging()
    {
        // 东八区 09:30 = UTC 01:30，属高峰；显示用本地时区，判定用 UTC
        DateTimeOffset beijing = new(2026, 10, 26, 9, 30, 0, TimeSpan.FromHours(8));

        IReadOnlyList<ProviderStatusNotice> notices = _calendar.Describe(Context("deepseek-v4.1-flash"), beijing);

        ProviderStatusNotice peak = Assert.Single(notices);
        Assert.Equal(EProviderNoticeKind.PeakPricing, peak.Kind);
        Assert.Equal(2, peak.Multiplier);
        Assert.Equal(Utc(4), peak.Until);
    }

    [Fact]
    public void NonDeepSeekModel_HasNoPeakNotice()
    {
        IReadOnlyList<ProviderStatusNotice> notices = _calendar.Describe(Context("kimi-k3"), Utc(2));

        Assert.Empty(notices);
    }

    [Fact]
    public void FreeModel_IsFlaggedAndBeatsPromotion()
    {
        IReadOnlyList<ProviderStatusNotice> notices = _calendar.Describe(Context("gpt-6-astra:free"), Utc(12));

        ProviderStatusNotice notice = Assert.Single(notices);
        Assert.Equal(EProviderNoticeKind.FreeModel, notice.Kind);
    }

    [Fact]
    public void DashedFreeSuffix_IsAlsoFree()
    {
        IReadOnlyList<ProviderStatusNotice> notices = _calendar.Describe(Context("glm-5-free"), Utc(12));

        Assert.Equal(EProviderNoticeKind.FreeModel, Assert.Single(notices).Kind);
    }

    [Fact]
    public void Promotion_ReportsItsMultiplier()
    {
        IReadOnlyList<ProviderStatusNotice> notices = _calendar.Describe(Context("minimax-m3"), Utc(12));

        ProviderStatusNotice notice = Assert.Single(notices);
        Assert.Equal(EProviderNoticeKind.PromoMultiplier, notice.Kind);
        Assert.Equal(2, notice.Multiplier);
    }

    [Fact]
    public void UnknownModel_SaysNothing()
    {
        Assert.Empty(_calendar.Describe(Context("mystery-model"), Utc(12)));
    }
}
