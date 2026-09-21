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
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Platform;
using Avalonia.Threading;
using SkiaSharp;
using UiharuMind.Core;
using UiharuMind.Core.AI.Chat;
using UiharuMind.Core.AI.Execution.Tools;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Resources.Lang;
using UiharuMind.Generated;
using UiharuMind.Shared.Services;
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
/// 状态表达按平台分开（见 <c>_isMacOS</c> 分叉）：
/// - macOS 走 template image + 动画：系统每帧按菜单栏实际明暗（壁纸亮度）自动着色，
///   所以<b>不需要探测明暗</b>，也绕开了「彩色与反色互斥」的 AppKit 限制。
///   素图是纯黑剪影 + alpha，正好满足 template 素材要求。状态用动画表达：
///   Idle 静态纯花，Running 旋转（12 帧），AwaitingApproval 晃动（左右摆）。
/// - Windows 没有反色机制，黑剪影在深色任务栏会看不见，保留彩色底图 +
///   右下角白环彩点（蓝=运行 / 橙=待审批）。
/// </summary>
public sealed class TrayStatusIndicator : IDisposable
{
    // 动画帧率与帧数。10fps 已是「在转」，再高只烧
    private const int RotationFrameCount = 12;
    private const int AnimIntervalMs = 100;

    private static readonly SKColor RunningColor = new(0x4C, 0x8D, 0xF6);
    private static readonly SKColor ApprovalColor = new(0xF2, 0x99, 0x3D);

    private readonly TrayIcon? _trayIcon;
    private readonly bool _isMacOs; //macOS: template + 动画;Windows: 彩色底图 + 角标

    // Windows 分支：状态 → 图标（显式映射，不依赖枚举底层值）
    private readonly Dictionary<ETrayStatus, WindowIcon?> _winIcons = new();

    // macOS 分支：静态 Idle + 两组动画帧
    private WindowIcon? _macIdle;
    private WindowIcon?[] _macRunningFrames = [];
    private WindowIcon?[] _macAlertFrames = [];
    private WindowIcon?[] _activeFrames = []; //当前在播的帧序列,OnAnimTick 只认它
    private readonly DispatcherTimer? _animTimer;
    private int _animIndex;

    private ETrayStatus _current = (ETrayStatus)(-1); //哨兵:首次 Refresh 必然不同,强制把图标落上去
    private bool _disposed;

    /// <param name="trayIcon">要驱动的那个托盘图标；为 null 表示这个平台上没有</param>
    /// <param name="baseIconUri">非 macOS 分支的彩色底图资源地址</param>
    public TrayStatusIndicator(TrayIcon? trayIcon, Uri baseIconUri)
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
            if (_isMacOs)
            {
                // 三态统一用纯花(镂空):Idle 静态,动画帧绕质心旋转/摆动
                _macIdle = IconUtils.LoadWindowIconFromAsset("TrayFlowerIdle.png");
                using SKBitmap flower = TrayFrameBuilder.DecodeAsset("TrayFlowerIdle.png");
                _macRunningFrames = TrayFrameBuilder.BuildRotationFrames(flower, RotationFrameCount);
                _macAlertFrames = TrayFrameBuilder.BuildWobbleFrames(flower);
                _animTimer = new DispatcherTimer(
                    TimeSpan.FromMilliseconds(AnimIntervalMs), DispatcherPriority.Normal, OnAnimTick);
            }
            else
            {
                using Stream source = AssetLoader.Open(baseIconUri);
                using SKBitmap bitmap = SKBitmap.Decode(source);
                _winIcons[ETrayStatus.Idle] = TrayFrameBuilder.ToWindowIcon(bitmap, null);
                _winIcons[ETrayStatus.Running] = TrayFrameBuilder.ToWindowIcon(bitmap, RunningColor);
                _winIcons[ETrayStatus.AwaitingApproval] = TrayFrameBuilder.ToWindowIcon(bitmap, ApprovalColor);
            }
        }
        catch (Exception e)
        {
            // 画不出来就退化成「只改 tooltip」:菜单栏图标丢一个角标远不如整个应用起不来严重
            Log.Warning($"Tray badge icons unavailable, falling back to tooltip only: {e.Message}");
        }

        SessionManager.Instance.Running.StateChanged += OnStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged += OnStateChanged;
        Refresh();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _animTimer?.Stop();
        SessionManager.Instance.Running.StateChanged -= OnStateChanged;
        BackgroundSubAgentDispatcher.PendingWorkChanged -= OnStateChanged;
    }

    /// <summary>重算一次并落到图标上。<b>可能来自后台线程</b>（子代理不在 UI 线程上）</summary>
    private void OnStateChanged(string _) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        if (_disposed || _trayIcon == null) return;

        // 口径是**进程里有没有活在跑**,不限于后台子代理:主会话自己那一轮、定时任务的无头轮次
        // 同样该亮。只看后台委派的话,用户自己发了条消息在等回复时菜单栏一片安静
        bool anyRunning = BackgroundSubAgentDispatcher.AnyPending();
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
        if (_isMacOs)
        {
            ApplyMacState(status);
        }
        else if (_winIcons.TryGetValue(status, out WindowIcon? icon))
        {
            _trayIcon.Icon = icon;
        }
        _trayIcon.ToolTipText = status switch
        {
            ETrayStatus.AwaitingApproval => Loc.Text(LangKey.SubAgentApprovalWaiting),
            ETrayStatus.Running => Loc.Text(LangKey.AgentStatusRunning),
            _ => AppInfo.Name,
        };
    }

    /// <summary>macOS 分支：按状态切静态图 / 启动对应动画。</summary>
    private void ApplyMacState(ETrayStatus status)
    {
        if (_trayIcon == null) return; //ctor 判过空;这里兜底供编译器可空分析

        // 状态→表现的一份映射：动画帧序列（Idle 无动画）
        _activeFrames = status switch
        {
            ETrayStatus.Running => _macRunningFrames,
            ETrayStatus.AwaitingApproval => _macAlertFrames,
            _ => [],
        };
        _animIndex = 0;
        _trayIcon.Icon = _activeFrames.Length > 0 ? _activeFrames[0] : _macIdle;
        if (_activeFrames.Length > 0) _animTimer?.Start(); else _animTimer?.Stop();
    }

    private void OnAnimTick(object? sender, EventArgs e)
    {
        if (_trayIcon == null) return; //timer 仅在 macOS 分支创建,彼时图标必然已就位
        if (_activeFrames.Length == 0) return;
        _animIndex = (_animIndex + 1) % _activeFrames.Length;
        if (_activeFrames[_animIndex] is { } icon) _trayIcon.Icon = icon;
    }
}
