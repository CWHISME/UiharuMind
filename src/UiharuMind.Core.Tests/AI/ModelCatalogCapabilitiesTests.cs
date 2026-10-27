using UiharuMind.Core.Configs.RemoteAI;
using UiharuMind.Core.RemoteOpenAI.ModelCatalog;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 目录条目 → 预填能力的合并优先级：预设变体 &gt; 目录自带字段 &gt; models.dev 静态表。
/// 目录是实际端点，优先于外部 models.dev；预设压在最上面（商汤目录把 kimi-k3 的
/// 采样/视觉/输出预算都标错了，靠预设显式值纠正）。
/// </summary>
public class ModelCatalogCapabilitiesTests
{
    /// <summary>预设显式声明压过目录：目录说纯文本/65536，预设说视觉/327680</summary>
    [Fact]
    public void Resolve_PresetWins()
    {
        var entry = new ModelCatalogEntry
        {
            Id = "deepseek-v4-flash",
            InputModalities = ["text"],
            OutputModalities = ["text"],
            MaxOutputLength = 65536,
            ContextLength = 1048576,
            SupportedSamplingParameters = ["temperature", "stop"],
        };
        var preset = new RemoteModelIdVariant(
            ContextLength: 1048576, MaxTokens: 327680, IsVision: true, OmitSamplingParams: true);

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, preset, "sensenova");

        Assert.True(resolved.IsVision);
        Assert.Equal(327680, resolved.MaxTokens);
        Assert.Equal(1048576, resolved.ContextLength);
        Assert.True(resolved.OmitSamplingParams);
    }

    /// <summary>没有预设：由 models.dev 静态表补齐（标准 /v1/models 只给 id）</summary>
    [Fact]
    public void Resolve_ModelsDevWhenNoPreset()
    {
        var entry = new ModelCatalogEntry { Id = "gemini-3-flash" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "opencode");

        Assert.True(resolved.IsVision);
        Assert.True(resolved.MaxTokens > 0);
        Assert.True(resolved.ContextLength > 0);
    }

    /// <summary>没有预设时目录压过 models.dev：目录说纯文本/1234，models.dev 说视觉/65536</summary>
    [Fact]
    public void Resolve_CatalogWinsOverModelsDev()
    {
        var entry = new ModelCatalogEntry
        {
            Id = "gemini-3-flash",
            InputModalities = ["text"],
            OutputModalities = ["text"],
            MaxOutputLength = 1234,
            ContextLength = 5678,
            SupportedSamplingParameters = ["temperature"],
        };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "opencode");

        Assert.False(resolved.IsVision);
        Assert.Equal(1234, resolved.MaxTokens);
        Assert.Equal(5678, resolved.ContextLength);
        Assert.False(resolved.OmitSamplingParams);
    }

    /// <summary>没有预设、也不查静态表（自定义服务商）：退回目录自带能力</summary>
    [Fact]
    public void Resolve_CatalogWhenNoPresetOrTable()
    {
        var entry = new ModelCatalogEntry
        {
            Id = "some-model",
            InputModalities = ["text", "image"],
            MaxOutputLength = 65536,
            ContextLength = 262144,
            SupportedSamplingParameters = ["stop"], // 不含 temperature → 固定采样
        };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "");

        Assert.True(resolved.IsVision);
        Assert.Equal(65536, resolved.MaxTokens);
        Assert.Equal(262144, resolved.ContextLength);
        Assert.True(resolved.OmitSamplingParams);
    }

    /// <summary>三源都没有：全零，由界面兜底</summary>
    [Fact]
    public void Resolve_EmptyEverything()
    {
        var entry = new ModelCatalogEntry { Id = "unknown" };

        ResolvedCapabilities resolved = ModelCatalogCapabilities.Resolve(entry, null, "");

        Assert.False(resolved.IsVision);
        Assert.Equal(0, resolved.MaxTokens);
        Assert.Equal(0, resolved.ContextLength);
        Assert.False(resolved.OmitSamplingParams);
    }
}
