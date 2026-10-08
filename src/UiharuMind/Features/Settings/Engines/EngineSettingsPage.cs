using Avalonia.Controls;
using UiharuMind.Features.Settings.Audio;
using UiharuMind.Features.Settings.Image;

namespace UiharuMind.Features.Settings.Engines;

/// <summary>
/// 设置里一个本地引擎的页：引擎卡 + 运行参数，左栏导航上带「有新版本」的点。加一种本地引擎就加一个工厂方法
/// </summary>
/// <param name="NavName">左栏按钮名（测试与跳转按名找）</param>
/// <param name="TitleKey">标题文案 key，也是搜索结果的页名</param>
/// <param name="IconName">左栏图标</param>
/// <param name="UpdateTipKey">更新点的悬停说明文案 key</param>
/// <param name="View">页面</param>
/// <param name="Engine">引擎卡（更新点看它的 HasUpdate）</param>
public sealed record EngineSettingsPage(
    string NavName,
    string TitleKey,
    string IconName,
    string UpdateTipKey,
    Control View,
    EngineVersionsViewData Engine)
{
    /// <summary>
    /// 本地模型（文本）
    /// </summary>
    public static EngineSettingsPage Text(RuntimeEngineSettingData data) => new("RuntimeButton",
        "RuntimeEngineSetting", "terminal", "SettingsEngineUpdateTip", new RuntimeEngineSettingView(data), data.Engine);

    /// <summary>
    /// 本地模型（语音）
    /// </summary>
    public static EngineSettingsPage Audio(AudioEngineSettingData data) => new("AudioRuntimeButton",
        "AudioEngineSetting", "audio-lines", "SettingsAudioEngineUpdateTip", new AudioEngineSettingView(data),
        data.Engine);

    /// <summary>
    /// 本地模型（生图）
    /// </summary>
    public static EngineSettingsPage Image(ImageEngineSettingData data) => new("ImageRuntimeButton",
        "ImageEngineSetting", "image", "SettingsImageEngineUpdateTip", new ImageEngineSettingView(data), data.Engine);
}
