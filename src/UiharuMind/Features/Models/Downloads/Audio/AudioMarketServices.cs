using UiharuMind.Core.AI.Audio;

namespace UiharuMind.Features.Models.Downloads.Audio;

/// <summary>
/// 语音模型分类要的：引擎（清单随它的 spec 走）、包的下载与删除、朗读 / 听写默认用哪个
/// </summary>
/// <param name="Engine">语音引擎</param>
/// <param name="Store">语音模型包的下载与删除</param>
/// <param name="Defaults">默认用本机哪个模型（与「语音模型」页签同一口径）</param>
public sealed record AudioMarketServices(AudioCppEngine Engine, AudioCppPackageStore Store, LocalAudioDefaults Defaults)
{
    /// <summary>
    /// 走应用档案的那一份
    /// </summary>
    public static AudioMarketServices CreateShared() => new(AudioCppEngine.Shared, AudioCppPackageStore.Shared,
        new LocalAudioDefaults(AudioCppEngine.Shared, AudioRouter.Shared));
}
