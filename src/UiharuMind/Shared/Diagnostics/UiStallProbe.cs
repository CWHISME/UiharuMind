/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using System.Diagnostics;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Diagnostics;

/// <summary>
/// UI 线程卡顿探针：按一帧的节拍空跑，记录相邻两次回调的间隔。
///
/// 它量的是<b>"卡"这件事本身</b>——用户说的卡就是掉帧，而不是某一段函数的耗时。
/// 回调排在 <see cref="DispatcherPriority.Background"/> 上，比布局与渲染都低，
/// 所以只要有人占着更高优先级不放，这里的间隔就会张开，张开多少就是掉了多久。
///
/// <para>
/// 与 <see cref="PageSwitchPerfProbe"/> 互补：那个量的是切页路径里的同步窗口，
/// 量不到点击到画面之间的全部开销，也量不到切页<b>之后</b>几帧里发生的事
/// （逐帧放行的 markdown、GC、渲染）。体感卡而那边数字漂亮，差额就在这里。
/// </para>
///
/// 默认关闭，置环境变量 <c>UIHARU_UI_STALL_PROBE=1</c> 开启——常驻一个每帧回调
/// 本身也是开销，不该让所有人替一次排查付账。
/// </summary>
public static class UiStallProbe
{
    private const int TickMs = 16; //约一帧
    private const double ReportThresholdMs = 80; //超过这个的间隔才算肉眼可见的一次卡

    private const string EnableVariable = "UIHARU_UI_STALL_PROBE";

    private static DispatcherTimer? _timer;
    private static long _lastTickTicks;

    /// <summary>探针是否开启。只在启动时读一次:它决定要不要挂定时器,中途翻转没有意义</summary>
    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable(EnableVariable) == "1";

    /// <summary>
    /// 开始监视。重复调用无副作用
    /// </summary>
    public static void Start()
    {
        if (!IsEnabled || _timer != null) return;

        _lastTickTicks = Stopwatch.GetTimestamp();
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(TickMs), DispatcherPriority.Background, OnTick);
        _timer.Start();
        Log.Debug($"[ui-stall] probe on, reporting gaps >= {ReportThresholdMs:F0}ms");
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        double gapMs = (now - _lastTickTicks) * 1000.0 / Stopwatch.Frequency;
        _lastTickTicks = now;

        if (gapMs >= ReportThresholdMs) Log.Debug($"[ui-stall] {gapMs:F0}ms");
    }
}
