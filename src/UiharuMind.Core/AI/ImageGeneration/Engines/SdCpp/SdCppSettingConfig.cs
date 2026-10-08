using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.ImageGeneration;

/// <summary>
/// stable-diffusion.cpp 引擎设置（照 AudioCppSettingConfig 的口径）。启动相关的改了，下次出图时按新设置重启
/// </summary>
public class SdCppSettingConfig : TConfigBase<SdCppSettingConfig>, IEngineVersionSelection
{
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
    /// VAE 分块解码：大图省显存
    /// </summary>
    public bool VaeTiling { get; set; }

    /// <summary>
    /// 闲置几分钟后停掉进程、还回内存；0 为不停
    /// </summary>
    public int IdleStopMinutes { get; set; } = 10;

    /// <summary>
    /// 启动加载权重最多等多少秒
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
