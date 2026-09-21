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
using UiharuMind.Core.AI.Runtime.Backends;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Diagnostics;

/// <summary>
/// 流式上屏性能探针：三个数一起看才有意义，所以收在同一处按固定间隔汇总打印。
///
/// <list type="bullet">
/// <item><b>上屏延迟</b>：首次追加增量 → 真正赋给可绑定属性。它量的是节拍器有没有被饿死，
/// 是判断「卡在哪」的主判据——远大于节拍值就说明问题在调度而不在布局。</item>
/// <item><b>布局延迟</b>：赋值 → 布局跑完。它量的是这一屏内容有多重。</item>
/// <item><b>条目数</b>：非虚拟化列表的成本正比于它，用来验证运行期裁剪是否生效。</item>
/// </list>
///
/// <b>只在 UI 线程调用</b>：采样点全部落在节拍器回调与布局回调上，因此内部不加锁。
/// 采样时刻的取用一律走 <see cref="Stopwatch"/> 时间戳，<c>Environment.TickCount64</c>
/// 在部分平台上的 ~15ms 粒度会把 50ms 量级的延迟量成阶梯。
/// </summary>
public static class StreamPerfProbe
{
    /// <summary>汇总打印间隔(毫秒)。逐次打印会把日志刷爆，而这些数只有分布有意义</summary>
    private const int ReportIntervalMs = 2000;

    /// <summary>探针是否开启。开关只在启动时读一次:它决定要不要挂事件,中途翻转没有意义</summary>
    public static bool IsEnabled { get; } = DebugSettingConfig.Current.IsConversationPerfProbeEnabled;

    private static readonly Sampler FlushLatency = new(); //首次追加 → 赋值
    private static readonly Sampler LayoutLatency = new(); //赋值 → 布局跑完
    private static int _itemCount;
    private static long _lastReportTicks;
    private static long _pendingFlushTicks; //待归因的赋值时刻;0 表示布局延迟无待归因样本

    /// <summary>
    /// 记一次上屏：从最早那条还没上屏的增量算到现在
    /// </summary>
    /// <param name="firstRequestedTicks">该条目最早一条未上屏增量的时间戳</param>
    public static void ReportFlushed(long firstRequestedTicks)
    {
        if (!IsEnabled) return;

        long now = Stopwatch.GetTimestamp();
        FlushLatency.Add(ToMilliseconds(now - firstRequestedTicks));
        // 同一节拍里多个条目一起冲刷，布局只跑一次，按最早那次赋值归因
        if (_pendingFlushTicks == 0) _pendingFlushTicks = now;
        Report(now);
    }

    /// <summary>
    /// 记一次布局跑完。没有待归因的赋值时不采样——那次布局不是流式内容引起的
    /// </summary>
    /// <param name="itemCount">当前会话列表的条目数</param>
    public static void ReportLayoutUpdated(int itemCount)
    {
        if (!IsEnabled) return;

        _itemCount = itemCount;
        if (_pendingFlushTicks == 0) return;

        long now = Stopwatch.GetTimestamp();
        LayoutLatency.Add(ToMilliseconds(now - _pendingFlushTicks));
        _pendingFlushTicks = 0;
        Report(now);
    }

    private static void Report(long now)
    {
        if (_lastReportTicks == 0) _lastReportTicks = now;
        if (ToMilliseconds(now - _lastReportTicks) < ReportIntervalMs) return;

        _lastReportTicks = now;
        if (FlushLatency.Count == 0 && LayoutLatency.Count == 0) return;

        Log.Debug($"[stream-perf] items={_itemCount} " +
                  $"flush={FlushLatency} layout={LayoutLatency}");
        FlushLatency.Reset();
        LayoutLatency.Reset();
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    /// <summary>一段区间内的样本累计。只留均值与峰值——峰值才是能被感知成"卡了一下"的那个数</summary>
    private sealed class Sampler
    {
        private double _sum;
        private double _max;

        public int Count { get; private set; }

        public void Add(double value)
        {
            Count++;
            _sum += value;
            if (value > _max) _max = value;
        }

        public void Reset()
        {
            Count = 0;
            _sum = 0;
            _max = 0;
        }

        public override string ToString() =>
            Count == 0 ? "-" : $"avg{_sum / Count:F1}/max{_max:F1}ms(n={Count})";
    }
}
