using UiharuMind.Core.AI.Engines;
using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp 引擎设置（照 LLamaCppSettingConfig 的口径）。改了启动相关项要让引擎重启才生效
/// </summary>
public class AudioCppSettingConfig : TConfigBase<AudioCppSettingConfig>, IEngineVersionSelection
{
    /// <summary>
    /// 选中的引擎版本名；空 = 用第一个装好的
    /// </summary>
    public string? SelectedRuntimeVersion { get; set; }

    /// <summary>
    /// 合成用的模型（本地模型名）；空或找不到 = 第一个语音合成模型
    /// </summary>
    public string? TtsModel { get; set; }

    /// <summary>
    /// 听写用的模型（本地模型名）；空或找不到 = 第一个语音识别模型
    /// </summary>
    public string? AsrModel { get; set; }

    /// <summary>
    /// 各合成模型选的音色（模型名 → 音色 id）。音色是模型自己的，换模型不能沿用
    /// </summary>
    public Dictionary<string, string> TtsVoices { get; set; } = new();

    /// <summary>
    /// 各模型改过的请求参数（模型名 → 参数名 → 值）；没改的不存，由模型用默认
    /// </summary>
    public Dictionary<string, Dictionary<string, string>> ModelOptions { get; set; } = new();

    /// <summary>
    /// 不用引擎包的 GPU 后端（Metal / Vulkan），一律跑 CPU。上游说 GPU 后端的模型覆盖不如 CUDA，个别模型加载报错时的退路
    /// </summary>
    public bool ForceCpu { get; set; }

    /// <summary>
    /// 加载模型后至少要剩多少内存（MB），不够就拒绝加载；0 = 不检查
    /// </summary>
    public int MinFreeMemoryMb { get; set; }

    /// <summary>
    /// CPU 线程数
    /// </summary>
    public int Threads { get; set; } = 4;

    /// <summary>
    /// 同时驻留的模型数，超出按最久未用卸载
    /// </summary>
    public int MaxLoadedModels { get; set; } = 2;

    /// <summary>
    /// 模型闲置多少分钟卸载
    /// </summary>
    public int IdleUnloadMinutes { get; set; } = 5;

    /// <summary>
    /// 启动等就绪的秒数（Metal 首次初始化较慢）
    /// </summary>
    public int ReadyTimeoutSeconds { get; set; } = 60;
}
