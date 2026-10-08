using UiharuMind.Core.AI.Engines;

namespace UiharuMind.Core.AI.Audio;

/// <summary>
/// 一份 audio.cpp 引擎包（发布号 × 平台变体）
/// </summary>
public class AudioCppVersion : EnginePackage
{
    /// <summary>
    /// 是否本机的推荐变体
    /// </summary>
    public override bool IsRecommended => AudioCppVariants.IsRecommended(Name);
}
