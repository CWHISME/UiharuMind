using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.AI.Models;
using UiharuMind.Core.AI.Runtime;
using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// 显卡：llama-server 报的设备与缓冲、独立显存下的加载风险、内存不够时上下文减半
/// </summary>
public class RuntimeGpuTests
{
    private const long Gib = 1024L * 1024 * 1024;
    private const long Mib = 1024L * 1024;

    [Fact]
    public void ListDevices_ParsesEveryBackendAndSkipsZeroMemory()
    {
        const string output = """
                              Available devices:
                                MTL0: Apple M2 Pro (12124 MiB, 12123 MiB free)
                                BLAS: Accelerate (0 MiB, 0 MiB free)
                                CUDA0: NVIDIA GeForce RTX 4090 (24563 MiB, 23012 MiB free)
                                Vulkan0: AMD Radeon RX 7900 XTX (RADV NAVI31) (24560 MiB, 20000 MiB free)
                              """;

        IReadOnlyList<GpuDevice> devices = GpuDeviceProbe.Parse(output);

        Assert.Equal(["MTL0", "CUDA0", "Vulkan0"], devices.Select(x => x.Id));
        Assert.Equal("AMD Radeon RX 7900 XTX (RADV NAVI31)", devices[2].Name);
        Assert.Equal(24563 * Mib, devices[1].TotalBytes);
        Assert.Equal(23012 * Mib, devices[1].FreeBytes);
    }

    [Fact]
    public void Probe_WithoutEngine_IsEmpty()
    {
        Assert.Empty(new GpuDeviceProbe(() => null).Devices(true));
    }

    [Fact]
    public void BufferTally_CountsDeviceBuffersOnly()
    {
        LLamaCppBufferTally tally = new();
        Assert.Null(tally.DeviceBytes);

        foreach (string line in new[]
                 {
                     "0.00.627.328 I load_tensors:   CPU_Mapped model buffer size =   304.28 MiB",
                     "0.00.627.329 I load_tensors:  CUDA0 model buffer size =  2048.00 MiB",
                     "0.00.629.630 I llama_context:        CPU  output buffer size =     2.32 MiB",
                     "0.00.884.504 I llama_kv_cache:      CUDA0 KV buffer size =  1024.00 MiB",
                     "0.00.884.600 I llama_kv_cache: size = 1024.00 MiB (  8192 cells,  36 layers)",
                     "0.01.067.960 I sched_reserve:       CUDA0 compute buffer size =   512.00 MiB",
                     "0.01.067.985 I sched_reserve:   CUDA_Host compute buffer size =    28.02 MiB",
                     "0.01.067.990 I load_tensors:  MTL0_Mapped model buffer size =   512.00 MiB"
                 })
            tally.Observe(line);

        Assert.Equal(4096 * Mib, tally.DeviceBytes);
    }

    [Fact]
    public void DedicatedGpu_FitsInVram_DoesNotNeedSystemMemory()
    {
        // 系统只剩 4 GB，但 24 GB 显卡放得下 6 GB 的模型
        RuntimeLoadRisk risk = Evaluate(-1, Device(4 * Gib, 20 * Gib));

        Assert.Equal(RuntimeLoadRiskLevel.Low, risk.Level);
    }

    [Fact]
    public void DedicatedGpu_SpillsToCpu_WarnsAndCountsOnlyTheSpill()
    {
        // 显存只空 3 GB（留 1 GB 余量后放 2 GB），剩下的挪回内存，内存够
        RuntimeLoadRisk enough = Evaluate(-1, Device(32 * Gib, 3 * Gib));
        // 内存也只剩 4 GB，挪回来的放不下
        RuntimeLoadRisk tight = Evaluate(-1, Device(4 * Gib, 3 * Gib));

        Assert.Equal(RuntimeLoadRiskLevel.Warning, enough.Level);
        Assert.Contains(enough.Warnings, x => x.Contains("VRAM"));
        Assert.Equal(RuntimeLoadRiskLevel.Danger, tight.Level);
    }

    [Fact]
    public void DedicatedGpu_ExplicitLayersBeyondVram_IsDanger()
    {
        RuntimeLoadRisk risk = Evaluate(32, Device(32 * Gib, 3 * Gib));

        Assert.Equal(RuntimeLoadRiskLevel.Danger, risk.Level);
    }

    [Fact]
    public void CpuOnly_IgnoresVram()
    {
        RuntimeLoadRisk risk = Evaluate(0, Device(4 * Gib, 20 * Gib));

        Assert.Equal(RuntimeLoadRiskLevel.Danger, risk.Level);
    }

    [Fact]
    public void HalveContext_StopsAt2048()
    {
        RuntimeResolvedParameters parameters = new(8192, 2048, 512, -1, 0, null, false, "");

        RuntimeResolvedParameters? half = RuntimeParameterResolver.HalveContext(parameters);
        RuntimeResolvedParameters? quarter = RuntimeParameterResolver.HalveContext(half!);

        Assert.Equal(4096, half!.ContextSize);
        Assert.True(half.WasAdjusted);
        Assert.Equal(2048, quarter!.ContextSize);
        Assert.Null(RuntimeParameterResolver.HalveContext(quarter));
    }

    [Theory]
    [InlineData("ggml_backend_cuda_buffer_type_alloc_buffer: allocating 9000 MiB on device 0: cudaMalloc failed: out of memory", true)]
    [InlineData("ggml_vulkan: Device memory allocation failed: ErrorOutOfDeviceMemory", true)]
    [InlineData("llama_kv_cache: failed to allocate buffer for kv cache", true)]
    [InlineData("gguf_init_from_file: failed to read magic", false)]
    public void OutOfMemory_IsRecognizedFromLog(string tail, bool expected)
    {
        Assert.Equal(expected, new LocalServerException("exited", tail).IsOutOfMemory);
    }

    [Fact]
    public void Verbosity_RaisedUnlessUserSetIt()
    {
        GGufModelInfo model = new() { ModelName = "m", ModelPath = "/models/m.gguf" };
        RuntimeResolvedParameters parameters = new(8192, 2048, 512, -1, 0, null, false, "");

        List<string> raised = [..LLamaCppServerArgs.Build(model, parameters, new LLamaCppServerOptions())];
        List<string> own = [..LLamaCppServerArgs.Build(model, parameters, new LLamaCppServerOptions { ExtraArguments = "-lv 2" })];

        Assert.Equal("4", raised[raised.IndexOf("-lv") + 1]);
        Assert.Single(own, x => x == "-lv");
        Assert.Equal("2", own[own.IndexOf("-lv") + 1]);
    }

    private static RuntimeDeviceInfo Device(long systemAvailable, long gpuFree) =>
        new(64 * Gib, systemAvailable, 24 * Gib, gpuFree, 0, "", "GPU", "CUDA0", DateTimeOffset.Now);

    // 6 GB 模型文件，元数据不给 KV（只算权重与小的计算缓冲）
    private static RuntimeLoadRisk Evaluate(int gpuLayers, RuntimeDeviceInfo device)
    {
        string path = Path.GetTempFileName();
        try
        {
            GGufModelInfo model = new() { ModelName = "m", ModelPath = path };
            ModelMetadata metadata = new()
            {
                FileSizeBytes = 6 * (ulong)Gib, LayerCount = 32, EmbeddingLength = 64, HeadCount = 1, KvHeadCount = 1
            };
            RuntimeResolvedParameters parameters = new(512, 512, 512, gpuLayers, 0, null, false, "",
                new RuntimeParameterRequest(512, 512, 512, gpuLayers, 0, false, true, true, true));
            return RuntimeLoadRiskEvaluator.Evaluate(model, metadata, parameters,
                new RuntimeParameterPolicy(RuntimeDeviceMode.Auto, true, false), device);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
