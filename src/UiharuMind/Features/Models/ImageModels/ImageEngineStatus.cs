using System;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型页签顶上的引擎状态条
/// </summary>
public static class ImageEngineStatus
{
    private static readonly EngineStatusTexts Texts = new(LangKey.ImageEngineStatusStarting,
        LangKey.ImageEngineStatusStopped, LangKey.ImageEngineStatusFailed, LangKey.ImageEngineNotInstalled);

    /// <summary>
    /// 跟着生图引擎的状态条：运行中那句说闲置多久自动停
    /// </summary>
    /// <param name="engine">生图引擎</param>
    /// <param name="openSettings">去引擎设置</param>
    /// <returns>状态条</returns>
    public static EngineStatusData Create(SdCppEngine engine, Action openSettings)
    {
        EngineStatusData status = new(engine.Runtime, Texts,
            () => engine.Settings.IdleStopMinutes > 0
                ? Loc.Text(LangKey.ImageEngineStatusRunning, engine.Settings.IdleStopMinutes)
                : Loc.Text(LangKey.ImageEngineStatusRunningNoIdle),
            openSettings: openSettings, isInstalled: () => engine.Distribution.CurrentVersion != null);
        engine.Distribution.VersionChanged += () => UiDispatcher.FireAndForget(status.Refresh);
        return status;
    }
}
