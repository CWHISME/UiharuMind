/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using System;
using System.Collections.Generic;
using Avalonia.Threading;
using UiharuMind.Shared.Windows;

namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// 窗口缓存：已关闭、隐藏待复用的窗口在这里驻留，由三道闸共同决定留多少、留多久。
///
/// <para>
/// 一扇隐藏着的窗口不是空壳：整棵控件树、样式实例、还有一块 GPU 表面都还在。
/// 所以三道闸都是内存闸，不是功能闸：
/// <list type="number">
/// <item><b>按类型限量</b>（<see cref="MaxCacheWindowCountPerType"/>）：
/// 防多开型窗口（文本编辑窗、全文窗）一类就占满内存；</item>
/// <item><b>全局限量</b>（<see cref="MaxCacheWindowCountTotal"/>）：
/// 窗口类型有十几种，只按类型限量的话总量仍然没有上界；</item>
/// <item><b>空闲淘汰</b>（<see cref="CacheIdleTimeout"/>）：
/// 前两道只在「又关了一扇」时才触发，应用放着不动就永远不收；这道把放久了的真关掉。</item>
/// </list>
/// </para>
///
/// <para>
/// 三道闸都是 LRU：淘汰最久没被复用的那扇，保证刚关的窗口总能进缓存。
/// </para>
/// </summary>
public static class WindowCache
{
    /// <summary>
    /// 每个窗口类型各自的缓存上限。按类型计而不是只看全局——主界面这类总是最后才关的窗口，
    /// 不该被先关掉的辅助窗挤掉缓存名额。
    /// </summary>
    public const int MaxCacheWindowCountPerType = 2;

    /// <summary>
    /// 全部类型合计的缓存上限。窗口类型有十几种，光按类型限量的话最坏情况是几十扇一起挂着。
    /// </summary>
    public const int MaxCacheWindowCountTotal = 4;

    /// <summary>隐藏后超过这个时长没被复用就真关掉：等它下次再开，比一直挂着划算</summary>
    public static readonly TimeSpan CacheIdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>扫过期的间隔。只求「放久了终会被收掉」，不求准点，间隔粗一点省唤醒</summary>
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    /// <summary>缓存中的窗口 → 最近一次进入缓存的时刻（单调毫秒）。既是 LRU 次序，也是过期依据</summary>
    private static readonly Dictionary<UiharuWindowBase, long> _cachedWindows = new();

    /// <summary>正在被淘汰的窗口：它们这次的 Close 必须真关，不能再收进缓存。</summary>
    private static readonly HashSet<UiharuWindowBase> _forceClosingWindows = new();

    private static DispatcherTimer? _sweepTimer;

    /// <summary>
    /// 把窗口收进缓存（关闭时隐藏、待下次复用）。超限时先按 LRU 淘汰再收下当前窗口，
    /// 所以刚关的这扇一定进得来。已在缓存里的窗口幂等：重复调用只刷新其新鲜度。
    /// </summary>
    public static bool TryCache(UiharuWindowBase win)
    {
        if (!_cachedWindows.ContainsKey(win))
        {
            if (CountOfType(win.GetType()) >= MaxCacheWindowCountPerType) EvictLeastRecentlyUsed(win.GetType());
            if (_cachedWindows.Count >= MaxCacheWindowCountTotal) EvictLeastRecentlyUsed(null);
        }

        _cachedWindows[win] = Environment.TickCount64;
        UpdateSweepTimer();
        return true;
    }

    /// <summary>
    /// 窗口重新显示时调用，把它从缓存集合里移出，让出一个缓存名额。
    /// </summary>
    public static void MarkShown(UiharuWindowBase win)
    {
        _cachedWindows.Remove(win);
        UpdateSweepTimer();
    }

    /// <summary>该窗口这次 Close 是否由缓存淘汰触发（必须真关，不能进缓存）。</summary>
    public static bool IsForceClosing(UiharuWindowBase win) => _forceClosingWindows.Contains(win);

    /// <summary>窗口销毁时调用，清掉它占用的缓存与强制关闭标记。</summary>
    public static void Drop(UiharuWindowBase win)
    {
        _cachedWindows.Remove(win);
        _forceClosingWindows.Remove(win);
        UpdateSweepTimer();
    }

    private static int CountOfType(Type type)
    {
        int count = 0;
        foreach (var cached in _cachedWindows.Keys)
        {
            if (cached.GetType() == type) count++;
        }

        return count;
    }

    /// <param name="type">限定在该类型里挑；null 表示不限类型，全局挑一扇</param>
    private static void EvictLeastRecentlyUsed(Type? type)
    {
        UiharuWindowBase? victim = null;
        long oldest = long.MaxValue;
        foreach (var pair in _cachedWindows)
        {
            if (type != null && pair.Key.GetType() != type) continue;
            if (pair.Value < oldest)
            {
                oldest = pair.Value;
                victim = pair.Key;
            }
        }

        if (victim != null) ForceClose(victim);
    }

    private static void ForceClose(UiharuWindowBase win)
    {
        _cachedWindows.Remove(win);
        _forceClosingWindows.Add(win);
        win.Close(); // 同步触发 OnClosing，走强制真关分支
        _forceClosingWindows.Remove(win);
    }

    // 缓存空了就把定时器停掉：应用大部分时间没有缓存窗口，不该为此每分钟醒一次
    private static void UpdateSweepTimer()
    {
        if (_cachedWindows.Count == 0)
        {
            _sweepTimer?.Stop();
            return;
        }

        _sweepTimer ??= new DispatcherTimer(SweepInterval, DispatcherPriority.Background, OnSweep);
        _sweepTimer.Start();
    }

    private static void OnSweep(object? sender, EventArgs e)
    {
        long deadline = Environment.TickCount64 - (long)CacheIdleTimeout.TotalMilliseconds;

        // 先挑齐再关：ForceClose 会改 _cachedWindows，不能边遍历边关
        List<UiharuWindowBase>? expired = null;
        foreach (var pair in _cachedWindows)
        {
            if (pair.Value <= deadline) (expired ??= new List<UiharuWindowBase>()).Add(pair.Key);
        }

        if (expired != null)
        {
            foreach (var win in expired) ForceClose(win);
        }

        UpdateSweepTimer();
    }
}
