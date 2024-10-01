using System.ComponentModel;
using UiharuMind.Core.Core.Attributes;
using UiharuMind.Core.Core.Configs;

namespace UiharuMind.Core.AI.LocalAI.LLamaCpp.Configs;

[DisplayName("CPU Config")]
public class LLamaCppServerCpuConfig : ConfigBase
{
    [SettingConfigDesc("number of threads to use during generation (default: -1)")]
    public int Threads { get; set; } = -1;

    [SettingConfigDesc("number of threads to use during batch and prompt processing (default:same as --threads)")]
    public int ThreadsBatch { get; set; } = -1;

    [SettingConfigDesc("use strict CPU placement (default: 0)")]
    //通过将线程或进程固定在特定的 CPU 核心上，减少了上下文切换和缓存未命中的开销，从而提高计算性能和一致性
    public bool CpuStrict { get; set; } = false;

    [SettingConfigDesc("use strict CPU placement (default: same as --cpu-strict)")]
    public bool CpuStrictBatch { get; set; } = false;

    [SettingConfigDesc("set process/thread priority : 0-normal, 1-medium, 2-high, 3-realtime (default: 0)")]
    [SettingConfigOptions("0", "1", "2", "3")]
    public string Prio { get; set; } = "";

    [SettingConfigDesc("set process/thread priority : 0-normal, 1-medium, 2-high, 3-realtime (default: 0)")]
    [SettingConfigOptions("0", "1", "2", "3")]
    public string PrioBatch { get; set; } = "";

    [SettingConfigDesc("use polling level to wait for work (0 - no polling, default: 50)")]
    [SettingConfigRange(0, 100)]
    public int Poll { get; set; } = 50;

    // [SettingConfigDesc("use polling to wait for work (default: same as --poll)")]
    // [SettingConfigRange(0, 100)]
    // public int PollBatch { get; set; } = 50;
}