using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models.ImageModels;

/// <summary>
/// 生图模型页签顶上的引擎状态：在不在跑、闲置多久自动停，跑着时可一键释放内存
/// </summary>
public partial class ImageEngineStatusData : ObservableObject
{
    private readonly IEngineRuntime _runtime;
    private readonly Func<int> _idleStopMinutes;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _text = "";

    /// <param name="runtime">生图引擎的进程</param>
    /// <param name="idleStopMinutes">闲置几分钟自动停（现取，设置里改了跟着变）</param>
    public ImageEngineStatusData(IEngineRuntime runtime, Func<int> idleStopMinutes)
    {
        _runtime = runtime;
        _idleStopMinutes = idleStopMinutes;
        _runtime.StateChanged += () => UiDispatcher.FireAndForget(Refresh);
        Refresh();
    }

    /// <summary>
    /// 按进程当前状态重写说明
    /// </summary>
    public void Refresh()
    {
        IsRunning = _runtime.State == EEngineRunState.Running;
        Text = _runtime.State switch
        {
            EEngineRunState.Starting => Loc.Text(LangKey.ImageEngineStatusStarting),
            EEngineRunState.Running => _idleStopMinutes() > 0
                ? Loc.Text(LangKey.ImageEngineStatusRunning, _idleStopMinutes())
                : Loc.Text(LangKey.ImageEngineStatusRunningNoIdle),
            EEngineRunState.Failed => Loc.Text(LangKey.ImageEngineStatusFailed, _runtime.LastError?.Message ?? ""),
            _ => Loc.Text(LangKey.ImageEngineStatusStopped)
        };
    }

    // 停掉引擎，模型占的内存全部还回去；下次出图自动再起
    [RelayCommand]
    private void Release() => _runtime.Stop();
}
