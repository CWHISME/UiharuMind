using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// audio.cpp 引擎设置（照 LLamaCppSettingConfig 的口径）。改了启动相关项要让引擎重启才生效
/// </summary>
public class AudioCppSettingConfig : TConfigBase<AudioCppSettingConfig>
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
