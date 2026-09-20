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
using UiharuMind.Shared.Windows;

namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// 窗口 LRU 缓存：已关闭、隐藏待复用的窗口按类型限量驻留，
/// 满了就淘汰同类型里最久没复用的一扇，保证刚关的窗口总能进缓存。
/// </summary>
public static class WindowCache
{
    /// <summary>
    /// 每个窗口类型各自的缓存上限：同一类型最多同时保留这么多扇「已关闭、隐藏待复用」的窗口。
    /// 按类型计而不是全局计——主界面这类总是最后才关的窗口，不该被先关掉的辅助窗挤掉缓存名额。
    /// 缓存满了之后按 LRU 淘汰同类型里最久没复用的那扇，让刚关的窗口进缓存，
    /// 防止多开型缓存窗（文本编辑窗、全文窗）无限驻留内存。
    /// </summary>
    public const int MaxCacheWindowCountPerType = 2;

    /// <summary>缓存中的窗口 → 最近一次进入缓存的访问序数（序数越小越久未用，即 LRU 淘汰目标）。</summary>
    private static readonly Dictionary<UiharuWindowBase, long> _cachedWindows = new();

    private static long _cacheAccessCounter;

    /// <summary>正在被 LRU 强制销毁的窗口：它们这次的 Close 必须真关，不能再收进缓存。</summary>
    private static readonly HashSet<UiharuWindowBase> _forceClosingWindows = new();

    /// <summary>
    /// 把窗口收进缓存（关闭时隐藏、待下次复用）。同类型缓存已满时，
    /// 先按 LRU 淘汰该类型最久没复用的一扇，再收下当前窗口。
    /// 已在缓存里的窗口幂等：重复调用只刷新其新鲜度。
    /// </summary>
    public static bool TryCache(UiharuWindowBase win)
    {
        if (_cachedWindows.ContainsKey(win))
        {
            _cachedWindows[win] = ++_cacheAccessCounter;
            return true;
        }

        int cachedForType = 0;
        foreach (var cached in _cachedWindows.Keys)
        {
            if (cached.GetType() == win.GetType()) cachedForType++;
        }

        if (cachedForType >= MaxCacheWindowCountPerType)
            EvictLeastRecentlyUsed(win.GetType());

        _cachedWindows[win] = ++_cacheAccessCounter;
        return true;
    }

    /// <summary>
    /// 窗口重新显示时调用，把它从缓存集合里移出，让出一个缓存名额。
    /// </summary>
    public static void MarkShown(UiharuWindowBase win) => _cachedWindows.Remove(win);

    /// <summary>该窗口这次 Close 是否由 LRU 强制触发（必须真关，不能进缓存）。</summary>
    public static bool IsForceClosing(UiharuWindowBase win) => _forceClosingWindows.Contains(win);

    /// <summary>窗口销毁时调用，清掉它占用的缓存与强制关闭标记。</summary>
    public static void Drop(UiharuWindowBase win)
    {
        _cachedWindows.Remove(win);
        _forceClosingWindows.Remove(win);
    }

    private static void EvictLeastRecentlyUsed(Type type)
    {
        UiharuWindowBase? victim = null;
        long oldest = long.MaxValue;
        foreach (var pair in _cachedWindows)
        {
            if (pair.Key.GetType() != type) continue;
            if (pair.Value < oldest)
            {
                oldest = pair.Value;
                victim = pair.Key;
            }
        }

        if (victim == null) return;

        _cachedWindows.Remove(victim);
        _forceClosingWindows.Add(victim);
        victim.Close(); // 同步触发 OnClosing，走强制真关分支
        _forceClosingWindows.Remove(victim);
    }
}
