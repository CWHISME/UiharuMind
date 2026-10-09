using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// stable-diffusion.cpp 引擎设置（照 AudioCppSettingConfig 的口径）。启动相关的改了，下次出图时按新设置重启
/// </summary>
public class SdCppSettingConfig : TConfigBase<SdCppSettingConfig>, IEngineVersionSelection
{
    // 16G 的机器上全进显存跑 FLUX.2 klein 实测能把系统换页换到看门狗重启
    private const long LowMemoryBytes = 24L * 1024 * 1024 * 1024;

    /// <summary>
    /// 本机内存不到 24G：省内存的几项默认打开
    /// </summary>
    public static bool IsLowMemoryMachine { get; } = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes is > 0 and < LowMemoryBytes;

    /// <summary>
    /// 选中的引擎版本名；空 = 用第一个装好的
    /// </summary>
    public string? SelectedRuntimeVersion { get; set; }

    /// <summary>
    /// 只用 CPU：不走 Metal / Vulkan（显卡驱动有问题时），会慢很多
    /// </summary>
    public bool ForceCpu { get; set; }

    /// <summary>
    /// CPU 线程数；0 或负数交给 sd.cpp（物理核数）
    /// </summary>
    public int Threads { get; set; } = -1;

    /// <summary>
    /// 权重放内存、用到哪块再搬进显存（--offload-to-cpu）：显存小的机器跑大模型用，慢一些
    /// </summary>
    public bool OffloadToCpu { get; set; }

    /// <summary>
    /// 扩散模型用 Flash Attention（--diffusion-fa）：省显存，多数情况也更快
    /// </summary>
    public bool DiffusionFlashAttention { get; set; } = true;

    /// <summary>
    /// VAE 分块解码：大图省显存。小内存机器默认开
    /// </summary>
    public bool VaeTiling { get; set; } = IsLowMemoryMachine;

    /// <summary>
    /// 文本编码器放 CPU（--clip-on-cpu）：它占的那几 G 不再锁进显存，系统吃紧时能换出去。小内存机器默认开
    /// </summary>
    public bool ClipOnCpu { get; set; } = IsLowMemoryMachine;

    /// <summary>
    /// 内存映射读权重（--mmap）：权重页随用随读、可被系统回收，常驻小得多。小内存机器默认开
    /// </summary>
    public bool MemoryMap { get; set; } = IsLowMemoryMachine;

    /// <summary>
    /// 闲置几分钟后停掉进程、还回内存；0 为不停
    /// </summary>
    public int IdleStopMinutes { get; set; } = 10;

    /// <summary>
    /// 启动加载权重时多少秒没有新输出算起不来（按停滞算）
    /// </summary>
    public int LoadTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// 一张图最多等多少秒（不含加载）
    /// </summary>
    public int GenerationTimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// 额外启动参数，排在最后，与上面的设置冲突时以它为准
    /// </summary>
    public string ExtraArguments { get; set; } = "";
}
