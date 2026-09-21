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
using System.Diagnostics;
using Avalonia.Threading;
using UiharuMind.Shared.Diagnostics;

namespace UiharuMind.Shared.Utils.Tools;

/// <summary>
/// 按节拍把流式缓冲冲上屏的条目
/// </summary>
public interface IStreamFlushTarget
{
    /// <summary>两次上屏之间至少间隔多少毫秒</summary>
    int FlushIntervalMs { get; }

    /// <summary>把缓冲同步到可绑定属性。<b>总在 UI 线程调用</b></summary>
    void FlushForDisplay();
}

/// <summary>
/// 流式上屏节拍器：全场一个 <see cref="DispatcherTimer"/>，节拍到点就把所有待冲刷的条目
/// 一次性上屏；没有待冲刷的条目时定时器停掉，静止期零开销。
///
/// 取代原先每个条目各自持一个 <c>ValueUiDelayUpdater</c> 的写法。那份实现有两处
/// 让长会话越卡越慢的病：
/// <list type="number">
/// <item><c>Task.Delay</c> 之后走 <c>DispatcherPriority.ApplicationIdle</c>(-4)，
/// 而渲染是 <c>Render</c>(4)、数据绑定是 <c>DataBind</c>(7)、输入是 <c>Input</c>(-1)——
/// 更新排在这些之后，会话一重就被饿死。</item>
/// <item>它 <c>await</c> 到 UI 动作执行完才解锁，于是真实节拍
/// = 延迟 + 排队 + 执行。越卡则更新越晚、攒的增量越大、单次重排越重，正反馈。</item>
/// </list>
///
/// 这里的口径反过来：<b>节拍到点就冲，不等上一次的渲染结果</b>。渲染慢是渲染的事，
/// 不该反过来拖慢取字。优先级取 <c>Normal</c>(8)，在渲染与绑定之上——冲刷本身只是
/// 一次 <c>ToString</c> 加一次属性赋值，不会反过来饿死布局。
///
/// 「全场一个泵 + 各自排队」与 <c>SimpleMarkdownViewer</c> 的 realize 队列是同一套写法。
/// </summary>
public static class StreamFlushPump
{
    /// <summary>基础节拍(毫秒)。各条目按自己的 <see cref="IStreamFlushTarget.FlushIntervalMs"/> 对齐到本节拍</summary>
    public const int TickMs = 50;

    private static readonly object Gate = new(); //Request 会从推理线程进来,Pending 必须上锁
    private static readonly Dictionary<IStreamFlushTarget, Pending> Waiting = new();
    private static readonly List<IStreamFlushTarget> DueBuffer = new(); //每拍复用,避免 20Hz 下的分配
    private static DispatcherTimer? _timer;

    /// <summary>
    /// 请求冲刷。可从任意线程调用；同一条目在到期之前重复请求只记一次
    /// </summary>
    /// <param name="target">待冲刷的条目</param>
    public static void Request(IStreamFlushTarget target)
    {
        long now = Stopwatch.GetTimestamp();
        lock (Gate)
        {
            // 已在排队:保留最早那次请求的时间戳,上屏延迟才量得到真实等待
            if (Waiting.ContainsKey(target)) return;
            long interval = MillisecondsToTicks(Math.Max(TickMs, target.FlushIntervalMs));
            Waiting[target] = new Pending(now, now + interval);
        }

        EnsureRunning();
    }

    private static void EnsureRunning()
    {
        if (Dispatcher.UIThread.CheckAccess()) Start();
        else Dispatcher.UIThread.Post(Start, DispatcherPriority.Normal);
    }

    private static void Start()
    {
        // Normal(8) 在 Render(4)/DataBind(7) 之上:取字不该被渲染饿死,这是本类存在的全部理由
        _timer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(TickMs), DispatcherPriority.Normal, OnTick);
        if (!_timer.IsEnabled) _timer.Start();
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        DueBuffer.Clear();

        lock (Gate)
        {
            foreach (KeyValuePair<IStreamFlushTarget, Pending> entry in Waiting)
            {
                if (entry.Value.DueTicks <= now) DueBuffer.Add(entry.Key);
            }
        }

        foreach (IStreamFlushTarget target in DueBuffer)
        {
            long requestedTicks;
            lock (Gate)
            {
                if (!Waiting.Remove(target, out Pending pending)) continue;
                requestedTicks = pending.RequestedTicks;
            }

            // 冲刷可能抛(绑定/转换器出错),但一个条目失败不该让整拍其余条目卡住
            try
            {
                target.FlushForDisplay();
            }
            finally
            {
                StreamPerfProbe.ReportFlushed(requestedTicks);
            }
        }

        // 空了就停:静止期不该有一个 20Hz 的定时器在空转
        lock (Gate)
        {
            if (Waiting.Count == 0) _timer?.Stop();
        }
    }

    private static long MillisecondsToTicks(int milliseconds) =>
        (long)(milliseconds * (Stopwatch.Frequency / 1000.0));

    /// <param name="RequestedTicks">最早一条未上屏增量的时间戳(探针据此量上屏延迟)</param>
    /// <param name="DueTicks">最早可以上屏的时刻</param>
    private readonly record struct Pending(long RequestedTicks, long DueTicks);
}
