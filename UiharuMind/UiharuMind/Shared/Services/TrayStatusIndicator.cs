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
/// 角标是<b>运行时画的</b>，不额外出三份图标资源：换一张图就要同步维护三份，
/// 而这里要的只是右下角一个点。
/// </summary>
public sealed class TrayStatusIndicator : IDisposable
{
    // 角标直径占图标边长的比例。macOS 会把整张图缩到 floor(菜单字号 * 1.333) ≈ 18px 高
    // (native/Avalonia.Native/src/OSX/trayicon.mm 的 SetIcon),所以这个点不能画小了——
    // 三分之一在 18px 上只剩 6px
    private const float BadgeDiameterRatio = 0.38f;
    private static readonly SKColor RunningColor = new(0x4C, 0x8D, 0xF6);
    private static readonly SKColor ApprovalColor = new(0xF2, 0x99, 0x3D);

    private readonly TrayIcon? _trayIcon;
    private readonly WindowIcon? _plain;
    private readonly WindowIcon? _running;
    private readonly WindowIcon? _approval;

    private ETrayStatus _current = ETrayStatus.Idle;
    private bool _disposed;

    /// <param name="trayIcon">要驱动的那个托盘图标；为 null 表示这个平台上没有</param>
    /// <param name="baseIconUri">底图资源地址</param>
    public TrayStatusIndicator(TrayIcon? trayIcon, Uri baseIconUri)
    {
        _trayIcon = trayIcon;
        if (_trayIcon == null) return;

        // 必须显式钉住:native 那边 _isTemplateIcon 是个**没有初始化**的成员
        // (trayicon.h 声明、AvnTrayIcon() 不赋值,而每次 SetIcon 都执行 [image setTemplate:]),
        // 于是"图标是彩色还是被抹成单色剪影"在没人设过时是不确定的。
        // 取 false 是因为底图是彩色 logo,开 template 会把它整个抹成剪影,角标的颜色也一起没了
        MacOSProperties.SetIsTemplateIcon(_trayIcon, false);

        try
        {
            using Stream source = AssetLoader.Open(baseIconUri);
            using SKBitmap bitmap = SKBitmap.Decode(source);
            _plain = ToWindowIcon(bitmap, null);
            _running = ToWindowIcon(bitmap, RunningColor);
            _approval = ToWindowIcon(bitmap, ApprovalColor);
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
        WindowIcon? icon = status switch
        {
            ETrayStatus.AwaitingApproval => _approval,
            ETrayStatus.Running => _running,
            _ => _plain,
        };
        if (icon != null) _trayIcon.Icon = icon;
        _trayIcon.ToolTipText = status switch
        {
            ETrayStatus.AwaitingApproval => Lang.SubAgentApprovalWaiting,
            ETrayStatus.Running => Lang.AgentStatusRunning,
            _ => AppInfo.Name,
        };
    }

    /// <summary>底图加一个右下角的点。<paramref name="badge"/> 为空就是原图</summary>
    private static WindowIcon ToWindowIcon(SKBitmap source, SKColor? badge)
    {
        using SKBitmap canvasBitmap = source.Copy();
        if (badge is { } color)
        {
            using SKCanvas canvas = new(canvasBitmap);
            float radius = Math.Min(canvasBitmap.Width, canvasBitmap.Height) * BadgeDiameterRatio / 2f;
            float center = radius + radius * 0.2f;
            using SKPaint ring = new() { Color = SKColors.White, IsAntialias = true };
            using SKPaint dot = new() { Color = color, IsAntialias = true };
            // 先画一圈白底再画点:菜单栏图标本身可能是浅色的,不垫底的话角标会糊进去
            canvas.DrawCircle(canvasBitmap.Width - center, canvasBitmap.Height - center, radius * 1.25f, ring);
            canvas.DrawCircle(canvasBitmap.Width - center, canvasBitmap.Height - center, radius, dot);
        }

        using SKData encoded = canvasBitmap.Encode(SKEncodedImageFormat.Png, 100);
        using MemoryStream stream = new(encoded.ToArray());
        return new WindowIcon(stream);
    }
}
