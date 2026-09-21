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

    /// <summary>
    /// DeepSeek 一类会同时报 hit 与 miss 两个键，字典遍历顺序不定——
    /// 只按 "cach" 子串找可能撞上 miss 的数。命中优先键必须显式匹配 hit。
    /// </summary>
    [Fact]
    public void CachedTokens_PreferHitOverMissWhenBothReported()
    {
        UsageDetails details = new()
        {
            InputTokenCount = 8000,
            AdditionalCounts = new()
            {
                ["prompt_cache_miss_tokens"] = 2000,
                ["prompt_cache_hit_tokens"] = 6000,
            },
        };

        Assert.Equal(6000, TurnUsageLedger.ReadCachedTokens(details));
    }

    [Fact]
    public void ReasoningTokens_AreRecordedPerResponseAndReset()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 8000, OutputTokenCount = 300, ReasoningTokenCount = 120 });

        Assert.Equal(120, ledger.LastReasoningTokens);

        // 没报这次数不能留着上一次的冒充
        ledger.Add(new UsageDetails { InputTokenCount = 9000, OutputTokenCount = 100 });
        Assert.Equal(0, ledger.LastReasoningTokens);

        ledger.Reset();
        Assert.Equal(0, ledger.LastReasoningTokens);
    }

    [Fact]
    public void SessionReasoningTokens_AccumulateAcrossResponses()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 8000, OutputTokenCount = 300, ReasoningTokenCount = 120 });
        ledger.Add(new UsageDetails { InputTokenCount = 9000, OutputTokenCount = 100, ReasoningTokenCount = 80 });

        Assert.Equal(200, ledger.SessionReasoningTokens);
    }

    [Fact]
    public void ReasoningTokens_DoNotAccumulateIntoSessionWhenUnreported()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(new UsageDetails { InputTokenCount = 8000, OutputTokenCount = 300, ReasoningTokenCount = 120 });
        // 下一次不报思考,累计数保持上一轮的值,不能追加一个 0 把它盖掉
        ledger.Add(new UsageDetails { InputTokenCount = 9000, OutputTokenCount = 100 });

        Assert.Equal(120, ledger.SessionReasoningTokens);
    }

    [Fact]
    public void RestoreSession_BringsBackReasoningAccumulation()
    {
        TurnUsageLedger ledger = new();
        ledger.RestoreSession(50_000, 3_000, 12_345, 456);

        Assert.Equal(456, ledger.SessionReasoningTokens);
    }

    [Fact]
    public void ReasoningTokens_AreZeroWhenTheProviderDoesNotReportThem()
    {
        UsageDetails details = new() { InputTokenCount = 8000 };

        Assert.Null(details.ReasoningTokenCount);
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

    /// <summary>
    /// 上限未知时仍要显示占用，只是没有分母。那个数是从会话本体恢复出来的，
    /// 「这个会话现在有多大」跟当前选没选模型无关——整段藏掉等于凭空少一条信息。
    /// </summary>
    [Fact]
    public void Text_ShowsOccupancyWithoutADenominatorWhenContextIsUnknown()
    {
        TurnUsageLedger ledger = new();
        ledger.Add(Usage(12_000, 800));

        Assert.Equal("12k", ledger.Text);
    }

    [Fact]
    public void Text_IsEmptyWhenThereIsNothingToShow()
    {
        Assert.Equal(string.Empty, new TurnUsageLedger().Text);
    }
}
