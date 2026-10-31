using System.Text.Json;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// models.dev 运行时快照：投影规则（与构建期生成器同一口径）、canonical 合并、缓存往返。
/// 这一层的数据来自外部，测试全部用构造的原始 JSON 片段，不依赖线上内容——线上数据天天变。
/// </summary>
public class ModelsDevSnapshotTests
{
    private static ModelsDevRawProvider Provider(string id, string? api, string npm = "@ai-sdk/openai-compatible",
        params (string Id, ModelsDevRawModel Model)[] models)
    {
        return new ModelsDevRawProvider
        {
            Id = id,
            Name = id.ToUpperInvariant(),
            Api = api,
            Doc = $"https://{id}.example/docs",
            Npm = npm,
            Models = models.ToDictionary(pair => pair.Id, pair => pair.Model),
        };
    }

    /// <summary>没有兼容地址的（走原生 SDK）与地址里带 ${VAR} 占位的都进不了列表：填进去必挂</summary>
    [Fact]
    public void Projection_DropsProvidersWithoutUsableAddress()
    {
        Dictionary<string, ModelsDevRawProvider> raw = new()
        {
            ["good"] = Provider("good", "https://api.good.example/v1"),
            ["noapi"] = Provider("noapi", null),
            ["templated"] = Provider("templated", "https://${HOST}/v1"),
        };

        ModelsDevCacheFile file = ModelsDevProjection.Build(raw, DateTimeOffset.Now, null);

        Assert.Equal(["good"], file.Providers.Select(provider => provider.Id));
    }

    /// <summary>视觉、固定采样参数、思考档位、价格（含第一档阶梯）逐项映射</summary>
    [Fact]
    public void Projection_MapsCapabilitiesAndPrice()
    {
        ModelsDevRawModel model = new()
        {
            Id = "vision-model",
            Modalities = new ModelsDevRawModalities { Input = ["text", "image"] },
            Limit = new ModelsDevRawLimit { Context = 200_000, Output = 64_000 },
            Temperature = false,
            ToolCall = true,
            Reasoning = true,
            ReasoningOptions =
            [
                new ModelsDevRawReasoningOption { Type = "effort", Values = ["low", "high", "low"] },
            ],
            Cost = new ModelsDevRawCost
            {
                Input = 3m,
                Output = 15m,
                CacheRead = 0.3m,
                Tiers = [new ModelsDevRawTier { Range = new ModelsDevRawTierRange { Size = 200_000 }, Input = 6m }],
            },
            CanonicalModelId = "vendor/vision-model",
        };

        ModelsDevCacheFile file = ModelsDevProjection.Build(
            new Dictionary<string, ModelsDevRawProvider> { ["p"] = Provider("p", "https://api.p.example/v1", "@ai-sdk/openai-compatible", ("vision-model", model)) },
            DateTimeOffset.Now, null);

        ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(file);
        Assert.True(snapshot.TryGetCapability("p", "vision-model", out ModelCapabilityTable.Capability capability));

        Assert.True(capability.IsVision);
        Assert.Equal(200_000, capability.ContextLength);
        Assert.Equal(64_000, capability.MaxTokens);
        Assert.True(capability.OmitSamplingParams);
        Assert.True(capability.SupportsToolCall);
        Assert.Equal("low,high", capability.Efforts);
        Assert.Equal(ThinkingControl.Effort, capability.Thinking);
        Assert.Equal(3m, capability.Price.Input);
        Assert.Equal(0.3m, capability.Price.CacheRead);
        Assert.Equal(200_000, capability.Price.TierFromContext);
        Assert.Equal(6m, capability.Price.TierInput);
    }

    /// <summary>只有开关、没有档位的思考声明不能被当成「有档位可选」</summary>
    [Fact]
    public void Projection_ToggleOnlyThinking_HasNoEfforts()
    {
        ModelsDevRawModel model = new()
        {
            Id = "toggle-model",
            ReasoningOptions = [new ModelsDevRawReasoningOption { Type = "toggle" }],
        };

        ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(ModelsDevProjection.Build(
            new Dictionary<string, ModelsDevRawProvider> { ["p"] = Provider("p", "https://api.p.example/v1", "@ai-sdk/openai-compatible", ("toggle-model", model)) },
            DateTimeOffset.Now, null));

        Assert.True(snapshot.TryGetCapability("p", "toggle-model", out ModelCapabilityTable.Capability capability));
        Assert.Equal(ThinkingControl.Toggle, capability.Thinking);
        Assert.Equal("", capability.Efforts);
    }

    /// <summary>同一 canonical 被多家收录时取并集：一家缺行，别家那行仍能回答「这是什么模型」</summary>
    [Fact]
    public void Snapshot_CanonicalMergesAcrossProviders()
    {
        ModelsDevRawModel plain = new() { Id = "m", CanonicalModelId = "vendor/m" };
        ModelsDevRawModel rich = new()
        {
            Id = "m",
            CanonicalModelId = "vendor/m",
            Modalities = new ModelsDevRawModalities { Input = ["image"] },
            Limit = new ModelsDevRawLimit { Context = 1_000_000, Output = 32_000 },
        };

        Dictionary<string, ModelsDevRawProvider> raw = new()
        {
            ["a"] = Provider("a", "https://a.example/v1", "@ai-sdk/openai-compatible", ("m", plain)),
            ["b"] = Provider("b", "https://b.example/v1", "@ai-sdk/openai-compatible", ("m", rich)),
        };

        ModelsDevSnapshot snapshot = ModelsDevSnapshot.FromCacheFile(ModelsDevProjection.Build(raw, DateTimeOffset.Now, null));

        Assert.True(snapshot.TryGetCanonical("vendor/m", out ModelCapabilityTable.Canonical canonical));
        Assert.True(canonical.IsVision);
        Assert.Equal(1_000_000, canonical.ContextLength);
    }

    /// <summary>落盘格式读回来必须一模一样：这是「下次启动不联网也有数据」的唯一保证</summary>
    [Fact]
    public async Task Catalog_LoadCache_RoundTrips()
    {
        ModelsDevRawModel model = new()
        {
            Id = "m1",
            Limit = new ModelsDevRawLimit { Context = 128_000, Output = 8_000 },
            Cost = new ModelsDevRawCost { Input = 1m, Output = 2m },
        };
        ModelsDevCacheFile file = ModelsDevProjection.Build(
            new Dictionary<string, ModelsDevRawProvider> { ["p"] = Provider("p", "https://api.p.example/v1", "@ai-sdk/openai-compatible", ("m1", model)) },
            new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero), "etag-1");

        string path = Path.Combine(Path.GetTempPath(), $"uiharu-modelsdev-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, ModelsDevJsonContext.Default.ModelsDevCacheFile),
                TestContext.Current.CancellationToken);

            ModelsDevCatalog catalog = new(path);
            await catalog.LoadCacheAsync();

            Assert.True(catalog.IsLoaded);
            Assert.Equal(file.FetchedAt, catalog.FetchedAt);
            RemoteProviderInfo provider = Assert.Single(catalog.Providers);
            Assert.Equal("p", provider.Id);
            Assert.Equal("https://api.p.example/v1", provider.ApiBaseUrl);
            Assert.Equal("https://api.p.example/v1/models", provider.CatalogUrl);
            Assert.Equal(1, provider.ModelCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>旧格式的缓存文件直接当没有：字段含义变过，读出来就是错的数据</summary>
    [Fact]
    public async Task Catalog_LoadCache_IgnoresStaleSchema()
    {
        ModelsDevCacheFile file = new()
        {
            SchemaVersion = ModelsDevCatalog.SchemaVersion + 1,
            FetchedAt = DateTimeOffset.Now,
            Providers = [new ModelsDevProviderDto { Id = "p", Name = "P", Api = "https://p.example/v1" }],
        };

        string path = Path.Combine(Path.GetTempPath(), $"uiharu-modelsdev-{Guid.NewGuid():N}.json");
        try
        {
            await File.WriteAllTextAsync(path, JsonSerializer.Serialize(file, ModelsDevJsonContext.Default.ModelsDevCacheFile),
                TestContext.Current.CancellationToken);

            ModelsDevCatalog catalog = new(path);
            await catalog.LoadCacheAsync();

            Assert.False(catalog.IsLoaded);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// models.dev 不给地址的（地址编进 SDK 包的那几家）靠 SDK 包名补回默认基址：
    /// 少了这条，OpenAI / Anthropic / Google / Groq / xAI 全会从列表里消失
    /// </summary>
    [Fact]
    public void Projection_FallsBackToSdkEndpoint()
    {
        Dictionary<string, ModelsDevRawProvider> raw = new()
        {
            ["groq"] = Provider("groq", null, "@ai-sdk/groq"),
            ["openai"] = Provider("openai", null, "@ai-sdk/openai"),
            ["unknown-sdk"] = Provider("unknown-sdk", null, "@someone/unknown"),
        };

        ModelsDevCacheFile file = ModelsDevProjection.Build(raw, DateTimeOffset.Now, null);

        Assert.Equal("https://api.groq.com/openai/v1", Assert.Single(file.Providers, p => p.Id == "groq").Api);
        Assert.Equal("https://api.openai.com/v1", Assert.Single(file.Providers, p => p.Id == "openai").Api);
        Assert.DoesNotContain(file.Providers, p => p.Id == "unknown-sdk");
    }

    /// <summary>anthropic 系走 Messages 线：地址是裸 /v1 认不出格式，只能靠 npm 给初值</summary>
    [Fact]
    public void ProviderInfo_AnthropicNpm_UsesMessagesLine()
    {
        RemoteProviderInfo anthropic = new("minimax", "MiniMax", "https://api.minimax.io/anthropic/v1", "", "@ai-sdk/anthropic", 12);
        RemoteProviderInfo compatible = new("groq", "Groq", "https://api.groq.com/openai/v1", "", "@ai-sdk/openai-compatible", 42);

        Assert.Equal(EApiFormat.Messages, anthropic.ApiFormat);
        Assert.Equal(EApiFormat.ChatCompletions, compatible.ApiFormat);
        Assert.Equal("https://api.groq.com/openai/v1/models", compatible.CatalogUrl);
    }
}
