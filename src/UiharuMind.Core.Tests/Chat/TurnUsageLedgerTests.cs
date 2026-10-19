using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Character;
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

        (long input, long output, long reasoning, long cached) = ledger.Add(Usage(10, 20));

        Assert.Equal(10, input);
        Assert.Equal(20, output);
        Assert.Equal(0, reasoning);
        Assert.Equal(0, cached);
        Assert.Equal(10, ledger.TurnInput);
        Assert.Equal(20, ledger.TurnOutput);
        Assert.Equal(10, ledger.SessionInput);
        Assert.Equal(20, ledger.SessionOutput);
        Assert.Equal(1, ledger.TurnRequests);
        Assert.Equal(1, ledger.SessionRequests);
    }

    /// <summary>
    /// 命中必须与输入<b>在同一处</b>累加。分子分母不同源时「累计命中率」是个没有意义的数：
    /// 分母含了全部请求、分子只含服务端报命中的那些，比例会凭空低一大截
    /// </summary>
    [Fact]
    public void Add_AccumulatesCachedOnTopOfTheSameInputScope()
    {
        TurnUsageLedger ledger = new();

        ledger.Add(new UsageDetails { InputTokenCount = 1000, CachedInputTokenCount = 900 });
        ledger.Add(new UsageDetails { InputTokenCount = 2000, CachedInputTokenCount = 500 });

        Assert.Equal(3000, ledger.SessionInput);
        Assert.Equal(1400, ledger.SessionCachedInput);
        Assert.Equal(3000, ledger.TurnInput);
        Assert.Equal(1400, ledger.TurnCachedInput);
        Assert.Equal(2000, ledger.LastInput); //单次那份仍是最后发的
        Assert.Equal(500, ledger.LastCachedInput);
    }

    /// <summary>新一轮清本轮命中，但累计留着——否则累计命中率每轮都从零重来</summary>
    [Fact]
    public void BeginTurn_ClearsTurnCachedButKeepsSessionCached()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 1000, CachedInputTokenCount = 900 });

        ledger.BeginTurn();

        Assert.Equal(0, ledger.TurnCachedInput);
        Assert.Equal(900, ledger.SessionCachedInput);
    }

    [Fact]
    public void Add_MissingCountsTreatedAsZero()
    {
        TurnUsageLedger ledger = new();

        (long input, long output, long reasoning, long cached) = ledger.Add(new UsageDetails());

        Assert.Equal(0, input);
        Assert.Equal(0, output);
        Assert.Equal(0, reasoning);
        Assert.Equal(0, cached);
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
        Assert.Equal(0, ledger.TurnRequests);
        Assert.Equal(1, ledger.SessionRequests);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(10, 20));
        ledger.CompactedInput = 500;

        ledger.Reset();

        Assert.Equal(0, ledger.SessionInput);
        Assert.Equal(0, ledger.SessionOutput);
        Assert.Equal(0, ledger.SessionCachedInput);
        Assert.Equal(0, ledger.TurnInput);
        Assert.Equal(0, ledger.TurnRequests);
        Assert.Equal(0, ledger.SessionRequests);
        Assert.Equal(0, ledger.CompactedInput); //留着会让新会话一开始就顶着旧会话折掉的那截
    }

    /// <summary>原始占用 = 有效占用 + 压掉的那截；没压时两者相等</summary>
    [Fact]
    public void RawInput_AddsTheCompactedSliceOnTopOfEffective()
    {
        TurnUsageLedger ledger = new() { EstimatedInput = 1000 };
        ledger.Add(Usage(3000, 0));
        Assert.Equal(ledger.EffectiveInput, ledger.RawInput);

        ledger.CompactedInput = 400;
        Assert.Equal(3400, ledger.RawInput);
    }

    /// <summary>累计按增量加：群后台的旁路调用也往同一本体上加，整值覆盖会抹掉那部分</summary>
    [Fact]
    public void Record_AddsTotalsOnTopOfTheSession()
    {
        ChatSession session = new("t", new CharacterData { CharacterId = "t" })
            { IsTransient = true, TotalInputTokens = 500, TotalOutputTokens = 600 };

        new TurnUsageLedger().Record(Usage(1, 2), session);

        Assert.Equal(501, session.TotalInputTokens);
        Assert.Equal(602, session.TotalOutputTokens);
        Assert.Equal(1, session.TotalRequests); //一次响应即一次请求
    }

    /// <summary>
    /// 群后台的旁路请求也把命中记进同一累计：它的输入进了分母，命中不跟着进，
    /// 那个比例就既不是命中率也没法跟官方控制台对账
    /// </summary>
    [Fact]
    public void AccumulateUsage_CarriesCachedSoGroupSideCallsStayInScope()
    {
        ChatSession session = new("t", new CharacterData { CharacterId = "t" }) { IsTransient = true };

        session.AccumulateUsage(1000, 10, 0, 950);

        Assert.Equal(1000, session.TotalInputTokens);
        Assert.Equal(950, session.TotalCachedInputTokens);
        Assert.Equal(1, session.TotalRequests); //群后台的旁路调用也计请求次数
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
        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        ledger.Add(Usage(100, 200));

        //本轮 ↑100 ↓200 与累计都不出现在状态栏,它们只在悬停面板里
        Assert.Equal("100/128k", ledger.Text);
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
