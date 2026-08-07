using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Chat;

namespace UiharuMind.Core.Tests.Chat;

/// <summary>
/// 钉死「上下文占用」的口径：分子是**最近一次**响应的输入 token，不是本轮累加值。
/// 一轮 agent 有十几次工具往返，累加值能到四十几万——那是成本视角，
/// 拿它当占用会让进度条瞬间爆表且永不回落。
/// </summary>
public class TurnUsageContextTests
{
    private static UsageDetails Usage(long input, long output) =>
        new() { InputTokenCount = input, OutputTokenCount = output };

    [Fact]
    public void LastInput_TracksTheLatestCallNotTheSum()
    {
        TurnUsageLedger ledger = new();

        ledger.Add(Usage(1000, 50));
        ledger.Add(Usage(1200, 60));
        ledger.Add(Usage(1500, 70));

        Assert.Equal(1500, ledger.LastInput); //占用:最后一次
        Assert.Equal(3700, ledger.TurnInput); //成本:累加
    }

    [Fact]
    public void BeginTurn_KeepsLastInput()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(1500, 70));

        ledger.BeginTurn();

        //新一轮尚未收到响应时,占用应仍显示上一轮的结果而不是掉回 0
        Assert.Equal(1500, ledger.LastInput);
        Assert.Equal(0, ledger.TurnInput);
    }

    [Fact]
    public void RestoreSession_BringsBackTheOccupancy()
    {
        TurnUsageLedger ledger = new();

        //切回一个老会话:占用随本体持久化,不该等到下一次响应才有数
        ledger.RestoreSession(50_000, 3_000, 12_345);

        Assert.Equal(12_345, ledger.LastInput);
    }

    [Fact]
    public void Reset_ClearsLastInput()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(1500, 70));

        ledger.Reset();

        Assert.Equal(0, ledger.LastInput); //换会话必须清,否则挂着上一个会话的占用
    }

    [Fact]
    public void ZeroInputResponse_DoesNotWipeTheOccupancy()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(1500, 70));

        ledger.Add(Usage(0, 30)); //有些兼容服务在流式增量里不带 usage

        Assert.Equal(1500, ledger.LastInput);
    }

    /// <summary>
    /// 前缀缓存有没有生效，只能看服务端报的这个数——推理不出来。
    /// 各家的键名不一样（OpenAI 系是 cached_tokens，经 MEAI 映射后还会再改名），
    /// 所以按子串命中而不是写死键名。
    /// </summary>
    [Theory]
    [InlineData("cached_tokens")]
    [InlineData("InputTokenCount.CachedTokenCount")]
    [InlineData("prompt_cache_hit_tokens")]
    public void CachedTokens_AreFoundWhateverTheKeyIsCalled(string key)
    {
        UsageDetails details = new()
        {
            InputTokenCount = 8000,
            AdditionalCounts = new() { [key] = 6000 },
        };

        Assert.Equal(6000, TurnUsageLedger.ReadCachedTokens(details));
    }

    [Fact]
    public void CachedTokens_AreZeroWhenTheProviderDoesNotReportThem()
    {
        UsageDetails details = new() { InputTokenCount = 8000 };

        Assert.Equal(0, TurnUsageLedger.ReadCachedTokens(details));
    }

    [Fact]
    public void CachedTokens_DoNotSurviveIntoAResponseThatOmitsThem()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 8000, AdditionalCounts = new() { ["cached_tokens"] = 6000 } });

        ledger.Add(new UsageDetails { InputTokenCount = 9000 });

        //留着上一次的数会让人以为这次也命中了缓存,那正是我们要测的东西
        Assert.Equal(0, ledger.LastCachedInput);
    }

    [Fact]
    public void Text_LeadsWithOccupancyWhenContextIsKnown()
    {
        TurnUsageLedger ledger = new() { ContextLength = 128_000 };
        ledger.Add(Usage(12_000, 800));

        Assert.StartsWith("12k/128k", ledger.Text);
    }

    [Fact]
    public void Text_OmitsOccupancyWhenContextIsUnknown()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(12_000, 800));

        Assert.DoesNotContain("/", ledger.Text);
    }
}
