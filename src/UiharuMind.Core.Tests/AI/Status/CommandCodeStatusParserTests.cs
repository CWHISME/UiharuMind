using UiharuMind.Core.AI.Status;
using UiharuMind.Core.AI.Status.Providers.CommandCode;

namespace UiharuMind.Core.Tests.AI.Status;

/// <summary>
/// CC 状态响应解析。
///
/// fixtures 里两份的分量不同，写清楚免得后人当成一回事：
/// - <c>commandcode-credits-empty.json</c> / <c>commandcode-whoami.json</c>：**2026-10-27 实测原文**
///   （id / 姓名 / 邮箱已脱敏）。这一份决定了「窗口在 <c>windowLimits</c> 下、余额是三个桶」这两个事实。
/// - <c>commandcode-credits-pro.json</c>：**假拟**——实测账户没有滚动窗口（<c>fiveHour: null</c>），
///   窗口对象内部的字段名至今没抓到真样本，这份只用来钉容错分支。
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
    }

    [Fact]
    public void Parse_WithoutCreditsBody_HasNothingToShow()
    {
        // 整个 credits 都没有 → 没有可显示的数字，上层按「这家没数据」隐藏
        Assert.Null(CommandCodeStatusParser.Parse(null, Fixture("commandcode-whoami.json"), Now));
        Assert.Null(CommandCodeStatusParser.Parse("{}", Fixture("commandcode-whoami.json"), Now));
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

    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "AI", "Status", "fixtures", name));
}
