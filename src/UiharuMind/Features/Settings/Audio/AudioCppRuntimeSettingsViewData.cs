using CommunityToolkit.Mvvm.ComponentModel;
using UiharuMind.Core.AI.Audio;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Settings.Audio;

/// <summary>
/// audiocpp_server 的启动参数。改一项即落盘；启动参数变了，引擎下次使用时自己换进程，在用的不打断
/// </summary>
public partial class AudioCppRuntimeSettingsViewData : ObservableObject
{
    private readonly AudioCppEngine _engine;
    private readonly SettingsWriteBack _writeBack;

    [ObservableProperty] private bool _forceCpu;
    [ObservableProperty] private decimal _minFreeMemoryMb;
    [ObservableProperty] private decimal _threads;
    [ObservableProperty] private decimal _maxLoadedModels;
    [ObservableProperty] private decimal _idleUnloadMinutes;
    [ObservableProperty] private decimal _readyTimeoutSeconds;

    /// <param name="engine">语音引擎（设置随它）</param>
    public AudioCppRuntimeSettingsViewData(AudioCppEngine engine)
    {
        _engine = engine;
        _writeBack = new SettingsWriteBack(Apply);
        AudioCppSettingConfig settings = engine.Settings;
        using (_writeBack.BeginLoad())
        {
            ForceCpu = settings.ForceCpu;
            MinFreeMemoryMb = settings.MinFreeMemoryMb;
            Threads = settings.Threads;
            MaxLoadedModels = settings.MaxLoadedModels;
            IdleUnloadMinutes = settings.IdleUnloadMinutes;
            ReadyTimeoutSeconds = settings.ReadyTimeoutSeconds;
        }
    }

    partial void OnForceCpuChanged(bool value) => _writeBack.Save();
    partial void OnMinFreeMemoryMbChanged(decimal value) => _writeBack.Save();
    partial void OnThreadsChanged(decimal value) => _writeBack.Save();
    partial void OnMaxLoadedModelsChanged(decimal value) => _writeBack.Save();
    partial void OnIdleUnloadMinutesChanged(decimal value) => _writeBack.Save();
    partial void OnReadyTimeoutSecondsChanged(decimal value) => _writeBack.Save();

    private void Apply()
    {
        AudioCppSettingConfig settings = _engine.Settings;
        settings.ForceCpu = ForceCpu;
        settings.MinFreeMemoryMb = (int)MinFreeMemoryMb;
        settings.Threads = (int)Threads;
        settings.MaxLoadedModels = (int)MaxLoadedModels;
        settings.IdleUnloadMinutes = (int)IdleUnloadMinutes;
        settings.ReadyTimeoutSeconds = (int)ReadyTimeoutSeconds;
        settings.Save();
    }
}
