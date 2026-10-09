using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// KV 缓存估算：GQA 只按 KV 头算，头数读不到才按全头算；单独给了 K/V 维度的按它算
/// </summary>
public class RuntimeKvEstimateTests
{
    private const double Gib = 1024d * 1024 * 1024;

    [Fact]
    public void Gqa_CountsOnlyKvHeads()
    {
        // mythos-nano（qwen2）：36 层、嵌入 2048、16 头里 2 个 KV 头，32K 上下文实际 KV 约 1.1 GB
        ModelMetadata gqa = new() { LayerCount = 36, EmbeddingLength = 2048, HeadCount = 16, KvHeadCount = 2 };
        ModelMetadata unknown = new() { LayerCount = 36, EmbeddingLength = 2048 };

        Assert.Equal(1.125, RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(gqa, 32768) / Gib, 3);
        Assert.Equal(9.0, RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(unknown, 32768) / Gib, 3);
    }

    [Fact]
    public void QuantizedCache_ShrinksByElementSize()
    {
        ModelMetadata gqa = new() { LayerCount = 36, EmbeddingLength = 2048, HeadCount = 16, KvHeadCount = 2 };
        long f16 = RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(gqa, 32768);
        long q8 = RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(gqa, 32768, 34 / 32d, 34 / 32d);
        long mixed = RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(gqa, 32768, 2, 18 / 32d);

        Assert.Equal(f16 * 17 / 32, q8);
        Assert.Equal(f16 / 2 + f16 / 2 * 9 / 32, mixed);
    }

    [Fact]
    public void ExplicitKeyValueLength_Wins()
    {
        // 头维度不等于嵌入 / 头数的架构（如 Gemma）
        ModelMetadata model = new()
        {
            LayerCount = 10, EmbeddingLength = 1000, HeadCount = 10, KvHeadCount = 1, KeyLength = 256,
            ValueLength = 128
        };

        Assert.Equal(100L * 10 * (256 + 128) * 2, RuntimeLoadRiskEvaluator.EstimateKvCacheBytes(model, 100));
    }

    [Fact]
    public void ExtraArguments_OverrideCacheType()
    {
        LLamaCppServerOptions options = new() { CacheTypeK = "q8_0", ExtraArguments = "--cache-type-v q4_0 -ctk f16" };

        Assert.Equal("f16", options.EffectiveCacheType(false));
        Assert.Equal("q4_0", options.EffectiveCacheType(true));
        Assert.Equal(18 / 32d, LLamaCppServerOptions.CacheTypeBytes(options.EffectiveCacheType(true)));
    }
}
