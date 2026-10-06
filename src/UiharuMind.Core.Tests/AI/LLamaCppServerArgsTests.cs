using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// llama-server 启动参数：默认值不传（让上游默认与 --fit 生效），改过的才传，额外参数排最后
/// </summary>
public class LLamaCppServerArgsTests
{
    private static readonly GGufModelInfo Model = new() { ModelName = "m", ModelPath = "/models/m.gguf" };

    private static RuntimeResolvedParameters Parameters(int gpuLayers = -1, bool? flashAttention = null) =>
        new(8192, 2048, 512, gpuLayers, 0, flashAttention, false, "");

    [Fact]
    public void Defaults_LeaveGpuLayersFlashAttentionAndServerDefaultsToLlamaServer()
    {
        List<string> args = [..LLamaCppServerArgs.Build(Model, Parameters(), new LLamaCppServerOptions())];

        Assert.DoesNotContain("-ngl", args);
        Assert.DoesNotContain("--flash-attn", args);
        Assert.DoesNotContain("--fit", args);
        Assert.DoesNotContain("-ctk", args);
        Assert.DoesNotContain("-cram", args);
        Assert.DoesNotContain("--load-mode", args);
        Assert.Equal("8192", args[args.IndexOf("-c") + 1]);
    }

    [Fact]
    public void CpuOnlyAndExplicitFlashAttention_ArePassed()
    {
        List<string> args = [..LLamaCppServerArgs.Build(Model, Parameters(0, false), new LLamaCppServerOptions())];

        Assert.Equal("0", args[args.IndexOf("-ngl") + 1]);
        Assert.Equal("off", args[args.IndexOf("--flash-attn") + 1]);
    }

    [Fact]
    public void ChangedOptions_ArePassed_AndExtraArgumentsComeLast()
    {
        LLamaCppServerOptions options = new()
        {
            Fit = false,
            ThreadsBatch = 6,
            Parallel = 2,
            ContinuousBatching = false,
            CacheTypeK = "q8_0",
            CacheTypeV = "q4_0",
            LoadMode = "mmap+mlock",
            NoKvOffload = true,
            CpuMoeLayers = 10,
            CacheRamMiB = -1,
            CacheReuse = 256,
            ContextShift = true,
            SwaFull = true,
            ExtraArguments = "--temp 0.7 --chat-template-file \"/a b/t.jinja\""
        };

        List<string> args = [..LLamaCppServerArgs.Build(Model, Parameters(), options)];

        Assert.Equal("off", args[args.IndexOf("--fit") + 1]);
        Assert.Equal("6", args[args.IndexOf("-tb") + 1]);
        Assert.Equal("2", args[args.IndexOf("-np") + 1]);
        Assert.Equal("q8_0", args[args.IndexOf("-ctk") + 1]);
        Assert.Equal("q4_0", args[args.IndexOf("-ctv") + 1]);
        Assert.Equal("10", args[args.IndexOf("-ncmoe") + 1]);
        Assert.Equal("-1", args[args.IndexOf("-cram") + 1]);
        Assert.Equal("256", args[args.IndexOf("--cache-reuse") + 1]);
        Assert.Contains("--no-cont-batching", args);
        Assert.Equal("mmap+mlock", args[args.IndexOf("--load-mode") + 1]);
        Assert.Contains("--no-kv-offload", args);
        Assert.Contains("--context-shift", args);
        Assert.Contains("--swa-full", args);
        Assert.Equal(["--temp", "0.7", "--chat-template-file", "/a b/t.jinja"], args[^4..]);
    }

    [Fact]
    public void FitTarget_OnlyWhenFitIsOnAndChanged()
    {
        List<string> args = [..LLamaCppServerArgs.Build(Model, Parameters(), new LLamaCppServerOptions { FitTargetMiB = 2048 })];

        Assert.Equal("2048", args[args.IndexOf("-fitt") + 1]);
    }

    [Fact]
    public void Environment_ParsesPairs_AndSkipsJunk()
    {
        IReadOnlyDictionary<string, string> env = LLamaCppServerArgs.ParseEnvironment("GGML_METAL_DEBUG=1; =x;broken\nA = b=c ");

        Assert.Equal(2, env.Count);
        Assert.Equal("1", env["GGML_METAL_DEBUG"]);
        Assert.Equal("b=c", env["A"]);
    }
}
