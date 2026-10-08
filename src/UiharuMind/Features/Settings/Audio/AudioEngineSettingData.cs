using System;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// 本地模型（语音）页：audio.cpp 引擎卡与运行参数。语音模型的下载、默认与音色在模型页的「语音模型」里管
/// </summary>
public sealed class AudioEngineSettingData
{
    public AudioEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>(), AudioCppEngine.Shared,
        path => App.FilesService.OpenFolder(path))
    {
    }

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">语音引擎</param>
    /// <param name="openFolder">打开目录</param>
    public AudioEngineSettingData(IMessageService messageService, AudioCppEngine engine, Action<string> openFolder)
    {
        Engine = new EngineVersionsViewData(engine.Distribution, "audio-lines", Loc.Text(LangKey.AudioEngineVersionDesc),
            messageService, openFolder);
        Runtime = new AudioCppRuntimeSettingsViewData(engine);
    }

    /// <summary>
    /// audio.cpp 引擎卡
    /// </summary>
    public EngineVersionsViewData Engine { get; }

    /// <summary>
    /// 运行参数
    /// </summary>
    public AudioCppRuntimeSettingsViewData Runtime { get; }
}
