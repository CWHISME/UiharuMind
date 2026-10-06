using UiharuMind.Core.AI.Models.Sources;
using UiharuMind.Core.AI.Runtime;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 一个仓库常有十几个量化：理清能下哪些、默认推哪个、这台机器跑不跑得动。
/// </summary>
public class ModelRepoLayoutTests
{
    private const long GiB = 1L << 30;

    [Theory]
    [InlineData("Qwen3-0.6B-Q4_K_M", "Q4_K_M")]
    [InlineData("qwen3-8b-q8_0", "Q8_0")]
    [InlineData("Qwen3-8B-UD-Q4_K_XL", "UD-Q4_K_XL")]
    [InlineData("gemma-3-12b-it-IQ4_XS", "IQ4_XS")]
    [InlineData("gpt-oss-20b-MXFP4", "MXFP4")]
    [InlineData("mmproj-BF16", "BF16")] //不能把 BF16 认成 F16
    [InlineData("mmproj-model-f16", "F16")]
    [InlineData("Qwen2.5-Coder-7B-Instruct", "")]
    public void ParsesQuantizationLabel(string name, string expected)
    {
        Assert.Equal(expected, ModelRepoLayout.ParseQuantization(name));
    }

    [Fact]
    public void GroupsShards_DropsIncompleteOnes_AndSeparatesProjectors()
    {
        ModelRepoLayout layout = ModelRepoLayout.From([
            File("README.md", 1),
            File("Q8_0/big-Q8_0-00002-of-00002.gguf", 4 * GiB),
            File("Q8_0/big-Q8_0-00001-of-00002.gguf", 5 * GiB),
            File("big-Q2_K-00001-of-00003.gguf", GiB), //缺片
            File("big-Q4_K_M.gguf", 5 * GiB),
            File("mmproj-F16.gguf", GiB / 2),
            File("mmproj-F32.gguf", GiB)
        ]);

        Assert.Equal(["big-Q4_K_M", "big-Q8_0"], layout.Quants.Select(x => x.Name));
        ModelRepoQuant q8 = layout.Quants[1];
        Assert.Equal("Q8_0", q8.Quantization);
        Assert.Equal(9 * GiB, q8.TotalSize);
        Assert.EndsWith("00001-of-00002.gguf", q8.Files[0].Path); //分片按片序
        Assert.Equal("F16", layout.PickDefaultProjector()?.Quantization);
    }

    [Fact]
    public void DefaultQuant_PrefersAFittingPreferredOne()
    {
        ModelRepoLayout layout = Layout("Q8_0", "Q4_K_M", "Q6_K", "Q2_K");

        ModelRepoQuant? picked = layout.PickDefaultQuant(_ => RuntimeLoadRiskLevel.Low);

        Assert.Equal("Q4_K_M", picked?.Quantization);
    }

    [Fact]
    public void DefaultQuant_FallsBackToTheLargestThatFits_WhenNoPreferredOneFits()
    {
        ModelRepoLayout layout = Layout("Q2_K", "Q3_K_M", "Q4_K_M", "Q8_0");

        ModelRepoQuant? picked = layout.PickDefaultQuant(x =>
            x.Quantization is "Q2_K" or "Q3_K_M" ? RuntimeLoadRiskLevel.Low : RuntimeLoadRiskLevel.Danger);

        Assert.Equal("Q3_K_M", picked?.Quantization);
    }

    [Fact]
    public void DefaultQuant_WhenNothingFits_StillOffersThePreferredOne()
    {
        ModelRepoLayout layout = Layout("Q2_K", "Q4_K_M", "Q8_0");

        Assert.Equal("Q4_K_M", layout.PickDefaultQuant(_ => RuntimeLoadRiskLevel.Danger)?.Quantization);
    }

    [Theory]
    [InlineData(4, 16, RuntimeLoadRiskLevel.Low)]
    [InlineData(9, 16, RuntimeLoadRiskLevel.Warning)]
    [InlineData(13, 16, RuntimeLoadRiskLevel.Danger)]
    public void BeforeDownload_TiersByTotalMemory_IgnoringWhatIsFreeRightNow(long modelGiB, long totalGiB,
        RuntimeLoadRiskLevel expected)
    {
        RuntimeDeviceInfo device = new(totalGiB * GiB, 1 * GiB, 0, 0, 0, "", "", "", DateTimeOffset.Now);

        Assert.Equal(expected, RuntimeLoadRiskEvaluator.EstimateBeforeDownload(modelGiB * GiB, 0, device));
    }

    [Fact]
    public void BeforeDownload_UnknownDevice_IsUnknown()
    {
        RuntimeDeviceInfo device = new(0, 0, 0, 0, 0, "", "", "", DateTimeOffset.Now);

        Assert.Equal(RuntimeLoadRiskLevel.Unknown, RuntimeLoadRiskEvaluator.EstimateBeforeDownload(GiB, 0, device));
    }

    private static ModelRepoFile File(string path, long size) => new(path, size, null);

    // 量化按给定顺序体积递增
    private static ModelRepoLayout Layout(params string[] quantizations) =>
        ModelRepoLayout.From(quantizations.Select((q, i) => File($"m-{q}.gguf", (i + 1) * GiB)));
}
