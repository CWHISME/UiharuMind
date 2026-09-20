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
/// 窗口注册表：登记所有已创建的 <see cref="UiharuWindowBase"/> 实例，
/// 含已关闭、隐藏待复用的缓存窗口。只负责「存哪些窗口」，复用与缓存的策略
/// 在 <see cref="UIManager"/> 与 <see cref="WindowCache"/>。
/// </summary>
public static class WindowRegistry
{
    private static readonly Dictionary<Type, List<UiharuWindowBase>> _multiWindows = new();

    /// <summary>
    /// 取某类型的窗口列表；不存在时创建空列表并登记。
    /// </summary>
    public static List<UiharuWindowBase> GetOrCreateList(Type type)
    {
        if (!_multiWindows.TryGetValue(type, out var windows))
        {
            windows = new List<UiharuWindowBase>();
            _multiWindows[type] = windows;
        }

        return windows;
    }

    /// <summary>
    /// 取某类型已创建的窗口列表；一个都没创建过时为空。
    /// </summary>
    public static IReadOnlyList<UiharuWindowBase> GetWindows(Type type)
    {
        return _multiWindows.TryGetValue(type, out var windows) ? windows : [];
    }

    /// <summary>
    /// 取某类型的第一个窗口；不存在时返回 null。
    /// </summary>
    public static UiharuWindowBase? FirstOrNull(Type type)
    {
        return _multiWindows.TryGetValue(type, out var windows) && windows.Count > 0 ? windows[0] : null;
    }

    /// <summary>
    /// 从注册表移除窗口，返回是否真的移除过。
    /// </summary>
    public static bool Remove(UiharuWindowBase win)
    {
        return _multiWindows.TryGetValue(win.GetType(), out var windows) && windows.Remove(win);
    }

    /// <summary>
    /// 遍历当前登记的全部窗口。
    /// </summary>
    public static IEnumerable<UiharuWindowBase> All()
    {
        foreach (var windows in _multiWindows.Values)
        {
            foreach (var win in windows)
            {
                yield return win;
            }
        }
    }
}
