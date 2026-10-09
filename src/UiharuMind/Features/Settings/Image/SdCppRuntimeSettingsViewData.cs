using System;
using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.ImageGeneration;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings.Image;

/// <summary>
/// sd-server 的启动参数与超时。改一项即落盘；启动参数变了，引擎下次出图时自己换进程，在算的不打断
/// </summary>
public partial class SdCppRuntimeSettingsViewData : ObservableObject
{
    private readonly SdCppEngine _engine;
    private readonly SettingsWriteBack _writeBack;

    [ObservableProperty] private bool _forceCpu;
    [ObservableProperty] private decimal _threads;
    [ObservableProperty] private bool _offloadToCpu;
    [ObservableProperty] private bool _diffusionFlashAttention;
    [ObservableProperty] private bool _vaeTiling;
    [ObservableProperty] private decimal _idleStopMinutes;
    [ObservableProperty] private decimal _loadTimeoutSeconds;
    [ObservableProperty] private decimal _generationTimeoutSeconds;
    [ObservableProperty] private string _extraArguments = "";

    /// <param name="engine">生图引擎（设置随它）</param>
    public SdCppRuntimeSettingsViewData(SdCppEngine engine)
    {
        _engine = engine;
        _writeBack = new SettingsWriteBack(Apply);
        SdCppSettingConfig settings = engine.Settings;
        using (_writeBack.BeginLoad())
        {
            ForceCpu = settings.ForceCpu;
            Threads = Math.Max(0, settings.Threads);
            OffloadToCpu = settings.OffloadToCpu;
            DiffusionFlashAttention = settings.DiffusionFlashAttention;
            VaeTiling = settings.VaeTiling;
            IdleStopMinutes = settings.IdleStopMinutes;
            LoadTimeoutSeconds = settings.LoadTimeoutSeconds;
            GenerationTimeoutSeconds = settings.GenerationTimeoutSeconds;
            ExtraArguments = settings.ExtraArguments;
        }
    }

    partial void OnForceCpuChanged(bool value) => _writeBack.Save();
    partial void OnThreadsChanged(decimal value) => _writeBack.Save();
    partial void OnOffloadToCpuChanged(bool value) => _writeBack.Save();
    partial void OnDiffusionFlashAttentionChanged(bool value) => _writeBack.Save();
    partial void OnVaeTilingChanged(bool value) => _writeBack.Save();
    partial void OnIdleStopMinutesChanged(decimal value) => _writeBack.Save();
    partial void OnLoadTimeoutSecondsChanged(decimal value) => _writeBack.Save();
    partial void OnGenerationTimeoutSecondsChanged(decimal value) => _writeBack.Save();
    partial void OnExtraArgumentsChanged(string value) => _writeBack.Save();

    private void Apply()
    {
        SdCppSettingConfig settings = _engine.Settings;
        settings.ForceCpu = ForceCpu;
        settings.Threads = Threads > 0 ? (int)Threads : -1;
        settings.OffloadToCpu = OffloadToCpu;
        settings.DiffusionFlashAttention = DiffusionFlashAttention;
        settings.VaeTiling = VaeTiling;
        settings.IdleStopMinutes = (int)IdleStopMinutes;
        settings.LoadTimeoutSeconds = (int)LoadTimeoutSeconds;
        settings.GenerationTimeoutSeconds = (int)GenerationTimeoutSeconds;
        settings.ExtraArguments = ExtraArguments.Trim();
        settings.Save();
    }
}
