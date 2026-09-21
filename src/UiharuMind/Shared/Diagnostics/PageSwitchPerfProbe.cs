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
using Avalonia;
using Avalonia.Layout;
using Avalonia.VisualTree;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.Diagnostics;

/// <summary>
/// 切页耗时探针：把一次切页拆成两段分别归因，因为这两段的解法完全不同。
///
/// <list type="bullet">
/// <item><b>激活</b>：<c>OnDisable</c> + <c>OnEnable</c> 的同步耗时。大就是某个页面在
/// 激活钩子里干了重活(文件 IO、起子进程、重建集合),该修的是那个页面自己。</item>
/// <item><b>布局</b>：目标页同步跑完一轮 measure/arrange 的耗时。</item>
/// <item><b>失效面</b>：布局前 measure 已失效的节点数／总数，分两个范围——<c>page</c>
/// 只算目标页，<c>view</c> 算整个 <c>MainView</c>。后者是必要的：<c>UpdateLayout()</c>
/// 跑的是整窗布局，而访问过的页面全都常驻视觉树，失效源可能在别页。</item>
/// <item><b>建树量</b>：<c>built</c> 是布局<b>之后</b>目标页的节点数。Avalonia 在首次
/// measure 时才展开模板，所以布局前只数得到根节点一个——
/// <c>page</c> 分母是 1 而 <c>built</c> 是几百，就说明这一次是<b>首次建树</b>，
/// 那笔钱是模板展开与样式套用；两者接近才是真正的<b>重排</b>。</item>
/// </list>
///
/// <b>量的是同步布局</b>：早先挂 <c>LayoutUpdated</c> 量到的是「下一次恰好有布局发生」
/// 的等待时间,不需要布局的切页会把无关的一轮布局算到自己头上,数字因此不可归因。
///
/// <b>只在 UI 线程调用</b>：采样点全落在切页路径与布局回调上，因此内部不加锁。
/// 只有超过 <see cref="ReportThresholdMs"/> 才打印——切页本来就该是无感的，
/// 记录每一次只会把日志刷爆。
/// </summary>
public static class PageSwitchPerfProbe
{
    /// <summary>打印门槛(毫秒)。低于它的切页不值得记录</summary>
    private const double ReportThresholdMs = 50;

    private static string _pageName = string.Empty;
    private static long _beginTicks;
    private static long _enabledTicks;
    private static long _layoutBeginTicks;
    private static bool _measureWasValid;
    private static int _invalidCount;
    private static int _nodeCount;
    private static int _viewInvalidCount;
    private static int _viewNodeCount;
    private static Visual? _page;

    /// <summary>是否有一次切页正在计时</summary>
    public static bool IsSwitchPending => _beginTicks != 0;

    /// <summary>
    /// 一次切页开始计时
    /// </summary>
    /// <param name="pageName">目标页名称,用于日志归因</param>
    public static void BeginSwitch(string pageName)
    {
        _pageName = pageName;
        _beginTicks = Stopwatch.GetTimestamp();
        _enabledTicks = 0;
    }

    /// <summary>
    /// 激活钩子已跑完
    /// </summary>
    public static void ReportEnabled()
    {
        if (_beginTicks == 0) return;
        _enabledTicks = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// 目标页的同步布局即将开始
    /// </summary>
    /// <param name="page">目标页</param>
    /// <param name="view">整个主视图，失效源可能在别的常驻页面里</param>
    public static void BeginLayout(Visual page, Visual view)
    {
        if (_beginTicks == 0) return;

        _page = page;
        _measureWasValid = page is Layoutable { IsMeasureValid: true };
        CountInvalidMeasure(page, out _invalidCount, out _nodeCount);
        CountInvalidMeasure(view, out _viewInvalidCount, out _viewNodeCount);
        _layoutBeginTicks = Stopwatch.GetTimestamp();
    }

    /// 统计子树里 measure 已失效的节点。只在探针启用且确实要打印时才走,
    /// 遍历本身相对那几十上百毫秒可以忽略
    private static void CountInvalidMeasure(Visual root, out int invalid, out int total)
    {
        invalid = 0;
        total = 0;
        foreach (Visual visual in root.GetSelfAndVisualDescendants())
        {
            if (visual is not Layoutable layoutable) continue;
            total++;
            if (!layoutable.IsMeasureValid) invalid++;
        }
    }

    /// <summary>
    /// 目标页布局已跑完，一次切页到此结束
    /// </summary>
    public static void ReportRendered()
    {
        if (_beginTicks == 0) return;

        long now = Stopwatch.GetTimestamp();
        long enabledTicks = _enabledTicks == 0 ? now : _enabledTicks;
        double activateMs = ToMilliseconds(enabledTicks - _beginTicks);
        double layoutMs = _layoutBeginTicks == 0 ? 0 : ToMilliseconds(now - _layoutBeginTicks);
        bool measureWasValid = _measureWasValid;
        int invalid = _invalidCount;
        int nodes = _nodeCount;
        int viewInvalid = _viewInvalidCount;
        int viewNodes = _viewNodeCount;
        int built = 0;
        if (_page != null) CountInvalidMeasure(_page, out _, out built);
        _page = null;
        _beginTicks = 0;
        _enabledTicks = 0;
        _layoutBeginTicks = 0;

        if (activateMs + layoutMs < ReportThresholdMs) return;
        Log.Debug($"[page-switch] {_pageName} activate={activateMs:F1}ms " +
                  $"layout={layoutMs:F1}ms valid={measureWasValid} " +
                  $"invalid={invalid}/{nodes}(page) {viewInvalid}/{viewNodes}(view) " +
                  $"built={built}");
    }

    private static double ToMilliseconds(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;
}
