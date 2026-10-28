/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using UiharuMind.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Spinner;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Shared.Services;

/// <summary>菜单栏图标要说的那件事。优先级即紧迫度：要你动手 &gt; 还在跑 &gt; 没事</summary>
public enum ETrayStatus
{
    /// <summary>没有后台委派在跑</summary>
    Idle,

    /// <summary>有后台委派在跑</summary>
    Running,

    /// <summary>有后台委派卡在审批上等人点选</summary>
    AwaitingApproval,
}

/// <summary>
/// 把「后台还有活/有活等你批」画到<b>菜单栏图标</b>上。
///
/// 存在的理由是<b>覆盖面</b>：应用内的三个入口（输入区横幅、右栏面板、左栏那一行）
/// 都要求用户正看着这个应用。菜单栏图标是唯一在用户切走之后还看得见的东西，
/// 而审批是有时限的——到期按拒绝收口，那次委派白跑（见 ADR 0025）。
///
/// 两个平台<b>同一套表现</b>：素图是纯黑剪影 + alpha，Idle 静态纯花，
/// Running 旋转（一圈 <see cref="SpinClock.FrameCount"/> 帧），AwaitingApproval 晃动（左右摆）。
/// 差别只在<b>墨色怎么来</b>：
/// - macOS 走 template image，系统每帧按菜单栏实际明暗（壁纸亮度）自动着色，素图原样喂进去即可，
///   也绕开了「彩色与反色互斥」的 AppKit 限制；
/// - Windows 没有反色机制，剪影染黑还是染白得自己判断，取<b>系统背景明暗</b>
///   （<see cref="IPlatformSettings.GetColorValues"/> 的 <c>ThemeVariant</c>，即设置里的「Windows 模式」，
///   不是应用自己的配色）——否则黑剪影落在深色任务栏上等于没有图标。系统切明暗时重染一遍。
/// </summary>
public sealed class TrayStatusIndicator : IDisposable
{
    // Windows 分支的墨色：浅色任务栏染黑、深色任务栏染白，与 macOS 上系统给 template 上的色一致
    private static readonly SKColor LightTaskbarInk = SKColors.Black;
    private static readonly SKColor DarkTaskbarInk = SKColors.White;

    private readonly TrayIcon? _trayIcon;
    private readonly bool _isMacOs; //macOS: template 自动着色;Windows: 自己按任务栏明暗染色
    private readonly IPlatformSettings? _platformSettings; //Windows: 系统切明暗时重染的订阅口

    private TrayFrameBuilder.Frames? _frames; //当前这套帧（Windows 上已按任务栏明暗染过）
    private WindowIcon[] _activeFrames = []; //当前在播的帧序列,OnAnimTick 只认它
    private bool _lightTaskbar; //Windows 任务栏是否浅色（macOS 不读）
    private bool _clockSubscribed; //是否已订阅 SpinClock;托盘与应用内转圈共用它,两边才转得一致
    private long _shownFrame = -1; //当前托盘上显示的帧号;定时器早到时帧号没变,同一帧不重复换图

    private ETrayStatus _current = (ETrayStatus)(-1); //哨兵:首次 Refresh 必然不同,强制把图标落上去
    private bool _disposed;

    /// <param name="trayIcon">要驱动的那个托盘图标；为 null 表示这个平台上没有</param>
    public TrayStatusIndicator(TrayIcon? trayIcon)
    {
        _trayIcon = trayIcon;
        if (_trayIcon == null) return;

        // 必须显式钉住:native 那边 _isTemplateIcon 是个**没有初始化**的成员
        // (trayicon.h 声明、AvnTrayIcon() 不赋值,而每次 SetIcon 都执行 [image setTemplate:]),
        // 于是"图标是彩色还是被抹成单色剪影"在没人设过时是不确定的
        _isMacOs = OperatingSystem.IsMacOS();
        MacOSProperties.SetIsTemplateIcon(_trayIcon, _isMacOs);

        try
        {
            if (!_isMacOs)
            {
                _platformSettings = Application.Current?.PlatformSettings;
                _lightTaskbar = IsLightTaskbar(_platformSettings);
            }

            RebuildFrames();
        }
        catch (Exception e)
        {
            // 画不出来就退化成「只改 tooltip」:菜单栏图标丢一个状态远不如整个应用起不来严重
            Log.Warning($"Tray icons unavailable, falling back to tooltip only: {e.Message}");
        }

        if (_platformSettings != null) _platformSettings.ColorValuesChanged += OnColorValuesChanged;

        SessionManager.Instance.Running.StateChanged += OnStateChanged;
        PendingWork.Changed += OnStateChanged;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SetClockSubscribed(false);
        if (_platformSettings != null) _platformSettings.ColorValuesChanged -= OnColorValuesChanged;
        SessionManager.Instance.Running.StateChanged -= OnStateChanged;
        PendingWork.Changed -= OnStateChanged;
    }

    /// <summary>重算一次并落到图标上。<b>可能来自后台线程</b>（子代理不在 UI 线程上）</summary>
    private void OnStateChanged(string _) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_disposed || _trayIcon == null) return;

        // 口径是**进程里有没有活在跑**,不限于后台子代理:主会话自己那一轮、定时任务的无头轮次
        // 同样该亮。只看后台委派的话,用户自己发了条消息在等回复时菜单栏一片安静
        bool anyRunning = PendingWork.Any();
        bool anyApproval = false;
        foreach (KeyValuePair<string, ESessionRunState> active in SessionManager.Instance.Running.ActiveSessions())
        {
            anyRunning = true;
            if (active.Value == ESessionRunState.AwaitingApproval) anyApproval = true;
        }

        ETrayStatus status = anyApproval ? ETrayStatus.AwaitingApproval
            : anyRunning ? ETrayStatus.Running
            : ETrayStatus.Idle;
        if (status == _current) return; //每次运行态变化都会喊一声,原样重设会让菜单栏图标闪

        _current = status;
        ApplyState(status);
        _trayIcon.ToolTipText = status switch
        {
            ETrayStatus.AwaitingApproval => Loc.Text(LangKey.SubAgentApprovalWaiting),
            ETrayStatus.Running => Loc.Text(LangKey.AgentStatusRunning),
            _ => AppInfo.Name,
        };
    }

    /// <summary>按状态换帧：Idle 静态、Running 转、AwaitingApproval 摆</summary>
    private void ApplyState(ETrayStatus status)
    {
        if (_trayIcon == null || _frames == null) return; //ctor 判过空;这里兜底供编译器可空分析

        // 状态→表现的一份映射：动画帧序列（Idle 无动画）
        _activeFrames = status switch
        {
            ETrayStatus.Running => _frames.Running,
            ETrayStatus.AwaitingApproval => _frames.Alert,
            _ => [],
        };
        _shownFrame = SpinClock.Frame;
        _trayIcon.Icon = _activeFrames.Length > 0 ? FrameAt(_shownFrame) : _frames.Idle;
        SetClockSubscribed(_activeFrames.Length > 0);
    }

    /// <summary>重新渲一套帧。macOS 用素图原样，Windows 先按任务栏明暗染色</summary>
    private void RebuildFrames()
    {
        using SKBitmap flower = SkiaBitmapUtils.DecodeAsset("TrayFlowerIdle.png");
        if (_isMacOs)
        {
            _frames = TrayFrameBuilder.Build(flower);
            return;
        }

        using SKBitmap inked = SkiaBitmapUtils.Tint(flower, _lightTaskbar ? LightTaskbarInk : DarkTaskbarInk);
        _frames = TrayFrameBuilder.Build(inked);
    }

    /// <summary>
    /// 系统切了明暗（Windows 上就是任务栏配色）。<b>不在 UI 线程上</b>，且切换很频繁
    /// （拖动滑块时一路报），所以先排到 UI 线程，再比对明暗有没有真变
    /// </summary>
    private void OnColorValuesChanged(object? sender, PlatformColorValues e) => Dispatcher.UIThread.Post(ApplyTaskbarTheme);

    private void ApplyTaskbarTheme()
    {
        if (_disposed) return;

        bool lightTaskbar = IsLightTaskbar(_platformSettings);
        if (lightTaskbar == _lightTaskbar) return;

        _lightTaskbar = lightTaskbar;
        try
        {
            RebuildFrames();
        }
        catch (Exception e)
        {
            Log.Warning($"Tray recolor failed: {e.Message}");
            return;
        }

        ApplyState(_current); //换了帧就得把图标重新落一次，否则要等下次状态变化才更新
    }

    /// <summary>任务栏是否浅色。取系统背景明暗，与应用自己的主题无关</summary>
    private static bool IsLightTaskbar(IPlatformSettings? settings)
    {
        if (settings == null) return false; //拿不到就按深色算：深色任务栏是 Windows 的默认

        try
        {
            return settings.GetColorValues().ThemeVariant != PlatformThemeVariant.Dark;
        }
        catch (Exception e)
        {
            Log.Warning($"Read system theme failed: {e.Message}");
            return false;
        }
    }

    private void SetClockSubscribed(bool subscribed)
    {
        if (subscribed == _clockSubscribed) return;
        _clockSubscribed = subscribed;
        if (subscribed) SpinClock.Ticked += OnAnimTick; else SpinClock.Ticked -= OnAnimTick;
    }

    // 帧号取自共用时钟而不是自己数:这样应用内转圈图标与本图标永远同相
    private WindowIcon FrameAt(long frame) => _activeFrames[(int)(frame % _activeFrames.Length)];

    private void OnAnimTick()
    {
        long frame = SpinClock.Frame;
        if (_trayIcon == null || _activeFrames.Length == 0 || frame == _shownFrame) return;

        _shownFrame = frame;
        _trayIcon.Icon = FrameAt(frame);
    }
}
