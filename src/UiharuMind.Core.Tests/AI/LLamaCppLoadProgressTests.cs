using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.Tests.AI;

/// <summary>
/// llama-server 加载进度按日志阶段走：真实日志的顺序下只增不减，无关行不动
/// </summary>
public class LLamaCppLoadProgressTests
{
    [Fact]
    public void RealLogOrder_IsIncreasing()
    {
        string[] lines =
        [
            "0.05.777.425 I cmn  common_param: device_info:",
            "0.05.780.787 I srv    load_model: loading model '/m.gguf'",
            "0.06.149.313 I load_tensors: loading model tensors, this can take a while... (load_mode = mmap)",
            "0.07.235.976 I load_tensors: MTL0_Mapped model buffer size =  1743.72 MiB",
            "0.07.295.384 I llama_kv_cache: size =  288.00 MiB (  8192 cells,  36 layers,  4/1 seqs)",
            "0.07.310.827 I sched_reserve: graph: nodes = 1233, splits = 2",
            "0.07.310.965 I cmn  common_init_: warming up the model with an empty run - please wait ...",
            "0.07.563.919 I srv    load_model: initializing, n_slots = 4, n_ctx_slot = 8192",
            "0.07.572.245 I srv  llama_server: model loaded"
        ];

        float[] progress = lines.Select(x => LLamaCppLoadProgress.Milestone(x) ?? -1).ToArray();

        Assert.All(progress, x => Assert.InRange(x, 0.01f, 0.95f));
        Assert.Equal(progress.OrderBy(x => x), progress);
        Assert.Null(LLamaCppLoadProgress.Milestone("0.07.238.651 I llama_context: n_ctx = 8192"));
    }
}
