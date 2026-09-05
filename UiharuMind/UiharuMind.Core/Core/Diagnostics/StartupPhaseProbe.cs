/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Diagnostics;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Core.Diagnostics;

/// <summary>
/// 启动分段计时：在启动路径上逐段打点，每段只报「距上一个打点过了多久」。
///
/// 存在的理由是启动那几秒里 UI 线程被占满，而
/// <c>UiStallProbe</c> 只能说出"掉了 519ms"，说不出是谁占的。这里把那段时间切成
/// 有名字的块，账就能落到具体一行代码上。
///
/// <para>默认关闭，置环境变量 <c>UIHARU_STARTUP_PROBE=1</c> 开启。</para>
/// </summary>
public static class StartupPhaseProbe
{
    private const string EnableVariable = "UIHARU_STARTUP_PROBE";

    /// <summary>低于这个的段不报——启动路径上多数打点之间本来就没事发生</summary>
    private const double ReportThresholdMs = 5;

    private static long _firstTicks;
    private static long _lastTicks;

    /// <summary>探针是否开启。只在启动时读一次</summary>
    public static bool IsEnabled { get; } =
        System.Environment.GetEnvironmentVariable(EnableVariable) == "1";

    /// <summary>
    /// 记一个启动阶段的结束点
    /// </summary>
    /// <param name="phase">阶段名，出现在日志里</param>
    public static void Mark(string phase)
    {
        if (!IsEnabled) return;

        long now = Stopwatch.GetTimestamp();
        if (_firstTicks == 0)
        {
            _firstTicks = now;
            _lastTicks = now;
            Log.Debug($"[startup] {phase} (t0)");
            return;
        }

        double deltaMs = ToMilliseconds(now - _lastTicks);
        double totalMs = ToMilliseconds(now - _firstTicks);
        _lastTicks = now;

        if (deltaMs < ReportThresholdMs) return;
        Log.Debug($"[startup] {phase} +{deltaMs:F0}ms (t={totalMs:F0}ms)");
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
