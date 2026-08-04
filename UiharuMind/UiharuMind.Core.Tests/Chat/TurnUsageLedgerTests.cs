using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// token 账本：本轮与会话累计的分别归零口径，以及显示文本的分段省略
/// </summary>
public class TurnUsageLedgerTests
{
    private static UsageDetails Usage(long input, long output) =>
        new() { InputTokenCount = input, OutputTokenCount = output };

    [Fact]
    public void Add_ReturnsDeltaAndAccumulatesBothScopes()
    {
        TurnUsageLedger ledger = new();

        (long input, long output) = ledger.Add(Usage(10, 20));

        Assert.Equal(10, input);
        Assert.Equal(20, output);
        Assert.Equal(10, ledger.TurnInput);
        Assert.Equal(20, ledger.TurnOutput);
        Assert.Equal(10, ledger.SessionInput);
        Assert.Equal(20, ledger.SessionOutput);
    }

    [Fact]
    public void Add_MissingCountsTreatedAsZero()
    {
        TurnUsageLedger ledger = new();

        (long input, long output) = ledger.Add(new UsageDetails());

        Assert.Equal(0, input);
        Assert.Equal(0, output);
        Assert.Equal(string.Empty, ledger.Text);
    }

    /// <summary>
    /// 新一轮只清本轮，会话累计必须留着——否则长会话的累计数字每轮都归零
    /// </summary>
    [Fact]
    public void BeginTurn_ClearsTurnOnly()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(10, 20));

        ledger.BeginTurn();

        Assert.Equal(0, ledger.TurnInput);
        Assert.Equal(0, ledger.TurnOutput);
        Assert.Equal(10, ledger.SessionInput);
        Assert.Equal(20, ledger.SessionOutput);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(10, 20));

        ledger.Reset();

        Assert.Equal(0, ledger.SessionInput);
        Assert.Equal(0, ledger.SessionOutput);
        Assert.Equal(0, ledger.TurnInput);
    }

    [Fact]
    public void RestoreSession_SetsAccumulatedWithoutTouchingTurn()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(1, 2));

        ledger.RestoreSession(500, 600);

        Assert.Equal(500, ledger.SessionInput);
        Assert.Equal(600, ledger.SessionOutput);
        Assert.Equal(1, ledger.TurnInput);
    }

    [Fact]
    public void Text_IsEmptyWhenNothingRecorded()
    {
        Assert.Equal(string.Empty, new TurnUsageLedger().Text);
    }

    [Fact]
    public void Text_InputEstimateOnly()
    {
        TurnUsageLedger ledger = new() { InputEstimate = 42 };

        Assert.Equal("≈42", ledger.Text);
    }

    [Fact]
    public void Text_TurnAndSessionSegments()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(100, 200));

        Assert.Equal("↑100 ↓200 (300)", ledger.Text);
    }

    [Fact]
    public void Text_AllThreeSegments()
    {
        TurnUsageLedger ledger = new() { InputEstimate = 5 };
        ledger.Add(Usage(100, 200));

        Assert.Equal("≈5  ↑100 ↓200 (300)", ledger.Text);
    }

    /// <summary>
    /// 本轮归零后累计段仍要显示——切轮次时数字不该整块消失
    /// </summary>
    [Fact]
    public void Text_KeepsSessionSegmentAfterBeginTurn()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(100, 200));
        ledger.BeginTurn();

        Assert.Equal("(300)", ledger.Text);
    }

    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(9999, "9999")]
    [InlineData(10000, "10k")]
    [InlineData(10500, "10.5k")]
    [InlineData(123456, "123.5k")]
    public void Format_SwitchesToKAtTenThousand(long count, string expected)
    {
        Assert.Equal(expected, TurnUsageLedger.Format(count));
    }
}
