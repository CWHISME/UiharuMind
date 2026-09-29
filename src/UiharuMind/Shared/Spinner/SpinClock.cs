using System;
using System.Diagnostics;
using Avalonia.Threading;

namespace UiharuMind.Shared.Spinner;

/// <summary>
/// 全应用共用的「转圈」时间轴：应用内的 <see cref="SpinnerIcon"/> 与菜单栏托盘图标读同一个时间，
/// 所以任何时刻两边的相位与转速都一致。
///
/// 不让各处自己转的原因：每个控件各起一段动画，起点取决于它何时挂上界面，相位天然对不齐。
/// 这里的时间来自进程级的 <see cref="Stopwatch"/>，与有没有人在用无关，
/// 所以后来才挂上的图标也直接落在正确的相位上。
///
/// 两边的精度不同：托盘图标只能逐帧换图（<see cref="Frame"/>，每帧 <see cref="IntervalMs"/>），
/// 应用内图标则按显示帧连续转（<see cref="Angle"/>）。二者读的是同一根时间轴，
/// 相差最多一帧的步长。
/// </summary>
public static class SpinClock
{
    /// <summary>转一圈的帧数（托盘换图的帧数）</summary>
    public const int FrameCount = 12;

    /// <summary>托盘每一帧的时长</summary>
    public const int IntervalMs = 100;

    private const int PeriodMs = FrameCount * IntervalMs;
    private const int PollIntervalMs = IntervalMs / 2; //比帧时长短一半,换图最多晚半帧

    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    private static Action? _ticked;
    private static DispatcherTimer? _timer;

    /// <summary>取当前毫秒数。默认走进程级秒表；测试替换它来固定时间</summary>
    internal static Func<double> ElapsedMs = () => Clock.Elapsed.TotalMilliseconds;

    /// <summary>当前是第几帧（单调递增）。各处按自己的序列长度取模，不同长度的序列也共用同一根时间轴</summary>
    public static long Frame => (long)(ElapsedMs() / IntervalMs);

    /// <summary>此刻的连续旋转角度（度），转一圈用 <c>FrameCount × IntervalMs</c> 毫秒</summary>
    public static float Angle => (float)(ElapsedMs() % PeriodMs / PeriodMs * 360);

    /// <summary>周期性触发，供只能逐帧换图的一方（托盘）轮询 <see cref="Frame"/>；有订阅者时才走表</summary>
    public static event Action Ticked
    {
        add
        {
            _ticked += value;
            _timer ??= new DispatcherTimer(
                TimeSpan.FromMilliseconds(PollIntervalMs), DispatcherPriority.Normal, (_, _) => _ticked?.Invoke());
            _timer.Start();
        }
        remove
        {
            _ticked -= value;
            if (_ticked == null) _timer?.Stop();
        }
    }
}
