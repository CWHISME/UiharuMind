using UiharuMind.Core.Configs;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 音频设置：TTS / ASR 各自默认用哪个 provider、默认音色。provider 的 Id 落盘；
/// 未注册时调用方落到第一个能跑的（与本地引擎的 LocalEngineId 同语义）。
/// 远程音频服务的凭据与条目不在这里——那是单独登记的模型配置（照生图模型先例，见 ADR 0052）。
/// </summary>
public class AudioSettingConfig : TConfigBase<AudioSettingConfig>
{
    /// <summary>合成默认用哪个 provider；空 = 第一个会合成的</summary>
    public string? PreferredTtsProviderId { get; set; }

    /// <summary>听写默认用哪个 provider；空 = 第一个会听写的</summary>
    public string? PreferredAsrProviderId { get; set; }
}
