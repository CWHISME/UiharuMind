using UiharuMind.Core.AI.Status;
using UiharuMind.Core.AI.Status.Providers.CommandCode;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// CC 状态响应解析。
///
/// fixtures 里三份的分量不同，写清楚免得后人当成一回事：
/// - <c>commandcode-credits-empty.json</c> / <c>commandcode-whoami.json</c>：**2026-10-27 实测原文**
///   （id / 姓名 / 邮箱已脱敏）。这一份决定了「窗口在 <c>windowLimits</c> 下、余额是三个桶」这两个事实。
/// - <c>commandcode-credits-real.json</c>：**2026-10-28 实测原文**（订阅中、窗口已开启）。
///   它钉住了当年假拟的那几个字段的真实名字与单位：上限叫 <c>cap</c>、重置时刻叫 <c>resetAt</c>、
///   而且 <c>resetAt</c> 是**毫秒**。
/// - <c>commandcode-credits-pro.json</c>：**假拟**——只用来钉容错分支（窗口字段名的宽松匹配）。
/// </summary>
public class CommandCodeStatusParserTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_RealEmptyAccount_ReadsZeroBalanceAndNoWindows()
    {
        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(
            Fixture("commandcode-credits-empty.json"), Fixture("commandcode-whoami.json"), Now);

        // 免费账户：没有滚动窗口（windowLimits 里全是 null），但余额确实读得出来（三个桶都是 0）
        Assert.NotNull(snapshot);
        Assert.Empty(snapshot!.Meters);
        Assert.Equal(0, snapshot.Balance?.Remaining);
        Assert.Null(snapshot.Balance?.PlanName);
        Assert.Equal("Command Code", snapshot.ProviderName);
        // whoami 的显示名：fixture 早先是双重编码的字符串，读不到；修好后这条才能真正盯住
        Assert.Equal("Fixture User", snapshot.AccountName);
    }

    [Fact]
    public void Parse_WithoutCreditsBody_HasNothingToShow()
    {
        // 整个 credits 都没有 → 没有可显示的数字，上层按「这家没数据」隐藏
        Assert.Null(CommandCodeStatusParser.Parse(null, Fixture("commandcode-whoami.json"), Now));
        Assert.Null(CommandCodeStatusParser.Parse("{}", Fixture("commandcode-whoami.json"), Now));
    }

    [Fact]
    public void Parse_RealPaidAccount_ReadsWindowsWithMillisecondResets()
    {
        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(
            Fixture("commandcode-credits-real.json"), null, Now);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Meters.Count);

        ProviderUsageMeter fiveHour = snapshot.Meters[0];
        Assert.Equal(14, fiveHour.Cap);
        Assert.Equal(0.0024884, fiveHour.Percent, 1e-7);
        // 毫秒当秒读会越界抛异常，整份快照就废了（ADR 0094 补记：真踩过）
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 15, 49, 12, 382, TimeSpan.Zero), fiveHour.ResetAt);
        Assert.Equal(new DateTimeOffset(2026, 11, 4, 10, 49, 12, 382, TimeSpan.Zero), snapshot.Meters[1].ResetAt);
    }

    [Fact]
    public void Parse_RealPaidAccount_KeepsTheThreeCreditBuckets()
    {
        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(
            Fixture("commandcode-credits-real.json"), null, Now);

        // 合计给顶栏读数，拆开的桶给明细面板——「本月还剩多少」只有桶答得出来
        double remaining = snapshot!.Balance!.Remaining!.Value;
        Assert.Equal(69.9651623326, remaining, 1e-9);
        IReadOnlyList<ProviderCreditBucket> buckets = snapshot.Balance.Buckets!;
        Assert.Equal(3, buckets.Count);
        Assert.Equal(EProviderCreditBucket.Monthly, buckets[0].Kind);
        Assert.Equal(69.9651623326, buckets[0].Amount, 1e-9);
        Assert.Equal(EProviderCreditBucket.Purchased, buckets[1].Kind);
        Assert.Equal(0, buckets[1].Amount);
        Assert.Equal(EProviderCreditBucket.Free, buckets[2].Kind);
    }

    [Fact]
    public void Parse_UnreadableResetTime_CostsOnlyThatField()
    {
        // 一个时间字段读不懂，代价不能是「整块数据都取不到」：余额与窗口照旧
        const string weird = """
            { "credits": { "monthlyCredits": 12 },
              "windowLimits": { "fiveHour": { "used": 1, "cap": 10, "resetAt": 999999999999999999 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(weird, null, Now);

        Assert.NotNull(snapshot);
        Assert.Null(Assert.Single(snapshot!.Meters).ResetAt);
        Assert.Equal(12, snapshot.Balance?.Remaining);
    }

    [Fact]
    public void Parse_SecondEpoch_IsNotMistakenForMilliseconds()
    {
        // 秒级数字仍在候选里（旧形状 / 其它上游），不能一律当毫秒砍到 1970 年
        const string seconds = """
            { "windowLimits": { "fiveHour": { "used": 1, "cap": 10, "resetAt": 1793202552 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(seconds, null, Now);

        Assert.Equal(new DateTimeOffset(2026, 10, 28, 15, 49, 12, TimeSpan.Zero),
            Assert.Single(snapshot!.Meters).ResetAt);
    }

    [Fact]
    public void Parse_ProShape_ReadsBothWindowsAndSummedBalance()
    {
        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(
            Fixture("commandcode-credits-pro.json"), null, Now);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Meters.Count);

        ProviderUsageMeter fiveHour = snapshot.Meters[0];
        Assert.Equal(EProviderMeterSlot.FiveHour, fiveHour.Slot);
        Assert.Equal(0.32, fiveHour.Percent, 4);
        Assert.Equal(new DateTimeOffset(2026, 10, 27, 13, 12, 0, TimeSpan.Zero), fiveHour.ResetAt);

        Assert.Equal(0.41, snapshot.Meters[1].Percent, 4);
        // 余额 = 月度 12.4 + 购买 5 + 赠送 0
        Assert.Equal(17.4, snapshot.Balance?.Remaining);
    }

    [Fact]
    public void Parse_FlatAndSnakeCase_StillWorks()
    {
        const string flat = """
            { "five_hour": { "used": "2", "limit": "8" }, "weekly": { "consumed": 5, "quota": 20 } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(flat, null, Now);

        Assert.NotNull(snapshot);
        Assert.Equal(2, snapshot!.Meters.Count);
        Assert.Equal(0.25, snapshot.Meters[0].Percent, 4);
        Assert.Equal(0.25, snapshot.Meters[1].Percent, 4);
    }

    [Fact]
    public void Parse_WindowWithoutLimit_IsSkipped()
    {
        const string noLimit = """
            { "windowLimits": { "fiveHour": { "used": 3 }, "weekly": { "used": 4, "limit": 40 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(noLimit, null, Now);

        Assert.NotNull(snapshot);
        Assert.Equal(EProviderMeterSlot.Weekly, Assert.Single(snapshot!.Meters).Slot);
    }

    [Fact]
    public void Parse_ExceededFlag_MarksTheNamedWindow()
    {
        const string exceeded = """
            { "windowLimits": {
                "exceeded": ["fiveHour"],
                "fiveHour": { "used": 9.5, "limit": 10 },
                "weekly": { "used": 1, "limit": 10 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(exceeded, null, Now);

        Assert.NotNull(snapshot);
        Assert.True(snapshot!.Meters[0].IsExhausted); //fiveHour 被点名
        Assert.False(snapshot.Meters[1].IsExhausted);
    }

    [Fact]
    public void Parse_ExceededTrue_MarksEveryWindow()
    {
        const string exceeded = """
            { "windowLimits": {
                "exceeded": true,
                "fiveHour": { "used": 9.5, "limit": 10 },
                "weekly": { "used": 9.9, "limit": 10 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(exceeded, null, Now);

        Assert.NotNull(snapshot);
        Assert.All(snapshot!.Meters, meter => Assert.True(meter.IsExhausted));
    }

    [Fact]
    public void Parse_BrokenBody_ReturnsNull()
    {
        Assert.Null(CommandCodeStatusParser.Parse("   ", null, Now));
        Assert.Null(CommandCodeStatusParser.Parse("not json", null, Now));
        Assert.Null(CommandCodeStatusParser.Parse("""{ "unrelated": 1 }""", null, Now));
    }

    [Fact]
    public void Parse_UnitFromBody_IsKept()
    {
        const string withUnit = """
            { "unit": "credits", "windowLimits": { "fiveHour": { "used": 1, "limit": 4 } } }
            """;

        ProviderStatusSnapshot? snapshot = CommandCodeStatusParser.Parse(withUnit, null, Now);

        Assert.NotNull(snapshot);
        Assert.Equal("credits", snapshot!.Meters[0].Unit);
    }

    [Fact]
    public void ParseSummary_RealBody_ReadsUsedAndPeriodStart()
    {
        ProviderUsageSummary? summary = CommandCodeStatusParser.ParseSummary(
            Fixture("commandcode-usage-summary-real.json"));

        Assert.NotNull(summary);
        Assert.Equal(0.0315770445, summary!.Used, 1e-9);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 10, 40, 16, TimeSpan.Zero), summary.PeriodStart);
        Assert.Equal(43, summary.RequestCount);
        Assert.Equal(100, summary.SuccessRate);
    }

    [Fact]
    public void ParseSummary_WithoutPeriodStart_IsUnusable()
    {
        // 缺周期起点就推不出重置日与月度分母，宁可整份不用
        Assert.Null(CommandCodeStatusParser.ParseSummary("""{ "totalCost": 1 }"""));
        Assert.Null(CommandCodeStatusParser.ParseSummary(null));
    }

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AI", "Status", "fixtures", name));
}
