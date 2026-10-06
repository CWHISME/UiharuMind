using System.Text.Json;
using UiharuMind.Core.AI.Embedding;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 嵌入用 llama-server：GPU 层数默认自动（不传），旧配置里的默认 0 不再把嵌入钉在 CPU 上
/// </summary>
public class LLamaCppEmbeddingArgsTests
{
    private static RuntimeResolvedParameters Parameters(int gpuLayers) => new(2048, 2048, 2048, gpuLayers, 0, false, false, "");

    [Fact]
    public void AutoGpuLayers_AreLeftToLlamaServer()
    {
        IReadOnlyList<string> args = LLamaCppEmbeddingSession.BuildArguments("/m/e.gguf", Parameters(-1));

        Assert.DoesNotContain("--gpu-layers", args);
        Assert.Contains("--embedding", args);
    }

    [Fact]
    public void CpuOnly_IsPassed()
    {
        List<string> args = [..LLamaCppEmbeddingSession.BuildArguments("/m/e.gguf", Parameters(0))];

        Assert.Equal("0", args[args.IndexOf("--gpu-layers") + 1]);
    }

    [Fact]
    public void Config_DefaultsToAuto_AndIgnoresOldCpuDefault()
    {
        EmbeddingModelSettingConfig old = JsonSerializer.Deserialize<EmbeddingModelSettingConfig>("""{"GpuLayers":0}""")!;

        Assert.Equal(-1, old.GpuLayers);
        Assert.Equal(-1, new EmbeddingModelSettingConfig().GpuLayers);
    }
}
