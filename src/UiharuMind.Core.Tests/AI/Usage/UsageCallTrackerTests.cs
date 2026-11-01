using Microsoft.Extensions.AI;
using UiharuMind.Core.AI.Core;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Usage;
using UiharuMind.Core.Configs.RemoteAI;

namespace UiharuMind.Core.Tests.AI.Usage;

/// <summary>
/// 埋点与计价的口径：终态一行、失败并量、重试计数、价格快照与本地/未定价的区分。
/// </summary>
public sealed class UsageCallTrackerTests
{
    [Fact]
    public void Complete_WritesOneRowWithTokensLatencyAndPriceSnapshot()
    {
        List<UsageRecord> records = [];
        UsageCallTracker tracker = UsageCallTracker.Begin("session-1", DeepSeekIdentity(), sink: records.Add);
        tracker.NoteFirstToken();
        tracker.NoteUsage(new UsageDetails
        {
            InputTokenCount = 1000,
            CachedInputTokenCount = 400,
            OutputTokenCount = 200,
            ReasoningTokenCount = 50,
            AdditionalCounts = new AdditionalPropertiesDictionary<long>
            {
                ["cache_creation_input_tokens"] = 123,
            },
        });
        tracker.Complete();

        UsageRecord record = Assert.Single(records);
        Assert.True(record.Ok);
        Assert.Equal("session-1", record.SessionId);
        Assert.Equal("DeepSeek 测试", record.ModelName);
        Assert.Equal("deepseek/deepseek-v4-flash", record.ModelId);
        Assert.Equal("commandcode", record.ProviderKey);
        Assert.Equal(1000, record.InputTokens);
        Assert.Equal(400, record.CachedRead);
        Assert.Equal(123, record.CachedWrite); //Anthropic 系的 cache_creation_input_tokens
        Assert.Equal(200, record.OutputTokens);
        Assert.Equal(50, record.ReasoningTokens);
        Assert.NotNull(record.LatencyMs);
        Assert.NotNull(record.FirstTokenMs);
        Assert.Equal(0, record.RetryCount);
        Assert.Equal(UsagePricing.SourceOfficial, record.PriceSource);
        Assert.Equal(0.15m, record.PriceInput);
        Assert.Equal(0.003m, record.PriceCacheRead);
        Assert.NotNull(record.CostUsd);
    }

    [Fact]
    public void Failure_AfterUsageReceived_MergesTokensIntoTheSameRow()
    {
        List<UsageRecord> records = [];
        UsageCallTracker tracker = UsageCallTracker.Begin("s", DeepSeekIdentity(), sink: records.Add);
        tracker.NoteUsage(new UsageDetails { InputTokenCount = 500, OutputTokenCount = 70 });
        tracker.Fail(UsageErrorKinds.Http, new HttpRequestException("boom", null,
            System.Net.HttpStatusCode.BadGateway));

        UsageRecord record = Assert.Single(records); //绝不另写第二行
        Assert.False(record.Ok);
        Assert.Equal(UsageErrorKinds.Http, record.ErrorKind);
        Assert.Equal(502, record.StatusCode);
        Assert.Equal(500, record.InputTokens);
        Assert.Equal(70, record.OutputTokens);
        Assert.Contains("boom", record.ErrorDetail);

        tracker.Complete(); //晚到的成功收口不许覆盖终态
        Assert.Single(records);
    }

    [Fact]
    public void ErrorKinds_ClassifyHttpStatusAndTimeouts()
    {
        HttpRequestException notFound = new("missing", null, System.Net.HttpStatusCode.NotFound);
        Assert.Equal(UsageErrorKinds.Http, UsageErrorKinds.Classify(notFound));
        Assert.Equal(404, UsageErrorKinds.StatusCodeOf(notFound));

        //取消包装成 OCE：分类只能靠调用方先判「用户是否按了停止」
        Assert.Equal(UsageErrorKinds.Timeout, UsageErrorKinds.Classify(new TaskCanceledException()));
        Assert.Equal(UsageErrorKinds.Stream, UsageErrorKinds.Classify(new InvalidOperationException("boom")));

        //嵌在里层也要能认出来（SDK 常把真正的 HTTP 失败包一层）
        Exception wrapped = new InvalidOperationException("outer", notFound);
        Assert.Equal(UsageErrorKinds.Http, UsageErrorKinds.Classify(wrapped));
        Assert.Equal(404, UsageErrorKinds.StatusCodeOf(wrapped));
    }

    [Fact]
    public void Failure_WithoutAnyUsage_LeavesLatencyNull()
    {
        List<UsageRecord> records = [];
        UsageCallTracker tracker = UsageCallTracker.Begin("s", DeepSeekIdentity(), sink: records.Add);
        tracker.Fail(UsageErrorKinds.Timeout);

        UsageRecord record = Assert.Single(records);
        Assert.Null(record.LatencyMs); //未测得不是 0
        Assert.Null(record.FirstTokenMs);
        Assert.Equal(0, record.InputTokens);
        Assert.Null(record.StatusCode);
    }

    [Fact]
    public void RetryTally_IsCarriedIntoTheRecord()
    {
        List<UsageRecord> records = [];
        UsageCallTracker tracker = UsageCallTracker.Begin("s", DeepSeekIdentity(), sink: records.Add);
        tracker.Retries.NoteRetry();
        tracker.Retries.NoteRetry();
        tracker.Complete();

        Assert.Equal(2, Assert.Single(records).RetryCount);
    }

    [Fact]
    public void CompleteWith_OneRowPerUsageAndTimingOnlyOnTheFirst()
    {
        List<UsageRecord> records = [];
        UsageCallTracker tracker = UsageCallTracker.Begin("s", DeepSeekIdentity(), sink: records.Add);
        tracker.Retries.NoteRetry();
        tracker.CompleteWith([
            new UsageDetails { InputTokenCount = 100, OutputTokenCount = 10 },
            new UsageDetails { InputTokenCount = 200, OutputTokenCount = 20 },
        ]);

        Assert.Equal(2, records.Count);
        Assert.Equal(100, records[0].InputTokens);
        Assert.Equal(200, records[1].InputTokens);
        Assert.NotNull(records[0].LatencyMs);
        Assert.Null(records[1].LatencyMs);
        Assert.Equal(1, records[0].RetryCount);
        Assert.Equal(0, records[1].RetryCount);
    }

    [Fact]
    public void LocalIdentity_IsUnpricedAndMarkedLocal()
    {
        ModelRunningData local = new(new GGufModelInfo { ModelName = "qwen-local" });
        UsageModelIdentity identity = UsageModelIdentity.From(local, DateTimeOffset.Now);

        Assert.True(identity.IsLocal);
        Assert.Equal("local", identity.ProviderId);
        Assert.Null(identity.Price);

        UsagePriceSnapshot price = identity.PriceFor(1000, 500, 100);
        Assert.Null(price.CostUsd); //本地不计价，不是 $0
        Assert.Null(price.Input);
    }

    [Fact]
    public void Pricing_AppliesCachePriceTierAndPeakMultiplier()
    {
        DateTimeOffset mondayPeak = PeakMoment();
        UsageModelIdentity identity = UsageModelIdentity.From(DeepSeekModel(), mondayPeak);
        Assert.Equal(2m, identity.Multiplier); //工作日 01:00-04:00 UTC 高峰 ×2

        UsagePriceSnapshot price = identity.PriceFor(1000, 400, 200);
        //(600×0.15 + 400×0.003 + 200×0.6) / 1e6 × 2
        decimal expected = (600 * 0.15m + 400 * 0.003m + 200 * 0.6m) / 1_000_000m * 2m;
        Assert.Equal(expected, price.CostUsd);
        Assert.Equal(2m, price.Multiplier);
        Assert.Equal(UsagePricing.SourceOfficial, price.Source);
    }

    [Fact]
    public void CacheSaved_IsZeroWhenCachePriceIsNotPublished()
    {
        Assert.Equal(0m, UsagePricing.CacheSaved(1000, 0.5m, null));
        Assert.Equal(0m, UsagePricing.CacheSaved(0, 0.5m, 0.1m));
        Assert.Equal(1000 * (0.5m - 0.1m) / 1_000_000m, UsagePricing.CacheSaved(1000, 0.5m, 0.1m));
    }

    [Fact]
    public void CachedWrite_IsNullWhenProviderDoesNotReportIt()
    {
        Assert.Null(UsageTokens.ReadCachedWriteTokens(new UsageDetails { InputTokenCount = 1 }));
        Assert.Null(UsageTokens.ReadCachedWriteTokens(new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["prompt_cache_hit_tokens"] = 7 },
        }));
        Assert.Equal(7, UsageTokens.ReadCachedWriteTokens(new UsageDetails
        {
            AdditionalCounts = new AdditionalPropertiesDictionary<long> { ["cache_creation_input_tokens"] = 7 },
        }));
    }

    private static DateTimeOffset PeakMoment()
    {
        DateTime today = DateTime.UtcNow.Date;
        while (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) today = today.AddDays(1);
        return new DateTimeOffset(today.AddHours(2), TimeSpan.Zero); //工作日 02:00 UTC
    }

    private static ModelRunningData DeepSeekModel() => new(new RemoteModelInfo
    {
        Config = new RemoteCommandCodeModelConfig
        {
            ModelName = "DeepSeek 测试",
            ModelId = "deepseek/deepseek-v4-flash",
            ModelPath = "https://api.commandcode.ai/provider/v1/chat/completions",
        },
    });

    private static UsageModelIdentity DeepSeekIdentity() =>
        UsageModelIdentity.From(DeepSeekModel(), DateTimeOffset.Now);
}
