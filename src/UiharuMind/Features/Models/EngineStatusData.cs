using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UiharuMind.Core.AI.Engines;
using UiharuMind.Features.Models.Usage;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.Models;

/// <summary>
/// 一种本地引擎状态条的文案（启动中、没在跑、起不来）
/// </summary>
/// <param name="Starting">启动中</param>
/// <param name="Stopped">没在跑</param>
/// <param name="Failed">起不来（{0} 为原因）</param>
public sealed record EngineStatusTexts(LangKey Starting, LangKey Stopped, LangKey Failed);

/// <summary>
/// 模型页签顶上的本地引擎状态（语音、生图共用）：在不在跑、启动到几成、为什么起不来，跑着时的实际占用与 CPU，可一键释放内存。
/// 起停跟着 <see cref="IEngineRuntime"/> 的事件走，占用要页签看得见时定时 <see cref="Refresh"/>；「运行中」那句各引擎自己给（闲置多久停、加载了几个模型…）
/// </summary>
public partial class EngineStatusData : ObservableObject
{
    private readonly IEngineRuntime _runtime;
    private readonly EngineStatusTexts _texts;
    private readonly Func<string> _runningText;
    private readonly Action _release;

    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private string _text = "";

    /// <param name="runtime">引擎进程</param>
    /// <param name="texts">文案</param>
    /// <param name="runningText">运行中那句（现取）</param>
    /// <param name="release">释放内存；null 为停掉引擎</param>
    public EngineStatusData(IEngineRuntime runtime, EngineStatusTexts texts, Func<string> runningText,
        Action? release = null)
    {
        _runtime = runtime;
        _texts = texts;
        _runningText = runningText;
        _release = release ?? runtime.Stop;
        _runtime.StateChanged += () => UiDispatcher.FireAndForget(Refresh);
        _runtime.LoadProgressChanged += () => UiDispatcher.FireAndForget(Refresh);
        Refresh();
    }

    /// <summary>
    /// 按进程当前状态重写说明（跑着的顺带取一次占用）
    /// </summary>
    public void Refresh()
    {
        IsRunning = _runtime.State == EEngineRunState.Running;
        Text = _runtime.State switch
        {
            EEngineRunState.Running when _runtime.SampleUsage() is { } usage =>
                $"{_runningText()} · {LocalUsageRow.UsageText(usage)}",
            EEngineRunState.Starting when _runtime.LoadProgress > 0 =>
                $"{Loc.Text(_texts.Starting)} {(int)(_runtime.LoadProgress * 100)}%",
            EEngineRunState.Starting => Loc.Text(_texts.Starting),
            EEngineRunState.Running => _runningText(),
            EEngineRunState.Failed => Loc.Text(_texts.Failed, _runtime.LastError?.Message ?? ""),
            _ => Loc.Text(_texts.Stopped)
        };
    }

    // 停掉引擎，模型占的内存全部还回去；下次用时自动再起
    [RelayCommand]
    private void Release()
    {
        _release();
        Refresh();
    }
}
