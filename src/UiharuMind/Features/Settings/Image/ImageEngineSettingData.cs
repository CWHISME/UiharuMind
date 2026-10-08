using System;
using Microsoft.Extensions.DependencyInjection;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Features.Settings.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Settings.Image;

/// <summary>
/// 本地模型（生图）页：stable-diffusion.cpp 引擎卡与运行参数。生图模型的下载、顺序与参数在模型页的「生图模型」里管
/// </summary>
public sealed class ImageEngineSettingData
{
    public ImageEngineSettingData() : this(App.Services.GetRequiredService<IMessageService>(), SdCppEngine.Shared,
        path => App.FilesService.OpenFolder(path))
    {
    }

    /// <param name="messageService">提示与确认</param>
    /// <param name="engine">生图引擎</param>
    /// <param name="openFolder">打开目录</param>
    public ImageEngineSettingData(IMessageService messageService, SdCppEngine engine, Action<string> openFolder)
    {
        Engine = new EngineVersionsViewData(engine.Distribution, "image", Loc.Text(LangKey.ImageEngineVersionDesc),
            messageService, openFolder);
        Runtime = new SdCppRuntimeSettingsViewData(engine);
    }

    /// <summary>
    /// stable-diffusion.cpp 引擎卡
    /// </summary>
    public EngineVersionsViewData Engine { get; }

    /// <summary>
    /// 运行参数
    /// </summary>
    public SdCppRuntimeSettingsViewData Runtime { get; }
}
