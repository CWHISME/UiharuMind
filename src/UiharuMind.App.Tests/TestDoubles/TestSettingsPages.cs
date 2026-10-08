using UiharuMind.Core.AI.Audio;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Features.Settings.Audio;
using UiharuMind.Features.Settings.Image;
using UiharuMind.Shared.Services;

namespace UiharuMind.App.Tests.TestDoubles;

/// <summary>
/// 设置窗口测试要的页数据：语音页接一个只在临时目录里、扫不到模型的引擎，不碰真实档案与网络
/// </summary>
internal static class TestSettingsPages
{
    /// <summary>
    /// 一份隔离的本地模型（语音）页数据
    /// </summary>
    /// <param name="messages">提示与确认</param>
    /// <returns>页数据</returns>
    public static AudioEngineSettingData Audio(IMessageService messages)
    {
        string root = Path.Combine(Path.GetTempPath(), $"uiharu-audio-page-{Guid.NewGuid():N}");
        AudioCppEngine engine = new(new AudioCppSettingConfig(), Path.Combine(root, "engines"),
            Path.Combine(root, "server.json"), () => []);
        return new AudioEngineSettingData(messages, engine, _ => { });
    }

    /// <summary>
    /// 一份隔离的本地模型（生图）页数据：引擎目录是空的临时目录
    /// </summary>
    /// <param name="messages">提示与确认</param>
    /// <returns>页数据</returns>
    public static ImageEngineSettingData Image(IMessageService messages)
    {
        string root = Path.Combine(Path.GetTempPath(), $"uiharu-image-page-{Guid.NewGuid():N}");
        return new ImageEngineSettingData(messages, new SdCppEngine(new SdCppSettingConfig(), root), _ => { });
    }
}
