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

    /// <summary>
    /// 状态栏只留「占用 + 输入估算」两段。本轮与会话累计已挪进悬停面板——
    /// 那一行紧挨发送按钮、位置很窄，四段堆进去会挤成一串看不出量级的数字。
    /// </summary>
    [Fact]
    public void Text_ShowsOccupancyAndEstimateOnly()
    {
        TurnUsageLedger ledger = new() { ContextLength = 128_000, InputEstimate = 5 };
        ledger.Add(Usage(8526, 200));

        Assert.Equal("8.5k/128k  ≈5", ledger.Text);
    }

    [Fact]
    public void Text_OmitsTurnAndSessionTotals()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(100, 200));

        Assert.Equal(string.Empty, ledger.Text); //没有上下文上限时占用段也省掉,整行为空
    }

    [Fact]
    public void Text_KeepsOccupancyAfterBeginTurn()
    {
        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        ledger.Add(Usage(8526, 200));
        ledger.BeginTurn();

        Assert.Equal("8.5k/128k", ledger.Text); //新一轮还没响应时,占用仍显示上一轮的结果
    }

    /// <summary>
    /// 千位折 k、百万位折 M。原先只有 k，于是 1M 上下文会显示成「1048.6k」——
    /// 位数一多就看不出量级了。悬停面板另走 FormatExact，那里要比大小，缩写反而碍事。
    /// </summary>
    [Theory]
    [InlineData(0, "0")]
    [InlineData(999, "999")]
    [InlineData(1000, "1k")]
    [InlineData(8526, "8.5k")]
    [InlineData(10500, "10.5k")]
    [InlineData(123456, "123.5k")]
    [InlineData(1_048_576, "1M")]
    [InlineData(1_500_000, "1.5M")]
    public void Format_SwitchesToKAtOneThousandAndMAtOneMillion(long count, string expected)
    {
        Assert.Equal(expected, TurnUsageLedger.Format(count));
    }
}
