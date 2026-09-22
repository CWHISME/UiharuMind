/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Shell;

namespace UiharuMind.Shared.Diagnostics;

/// <summary>
/// 自动切页压测：按固定间隔轮着跳页，用来在没有人点鼠标的情况下复现"切页卡顿"。
///
/// 存在的理由是<b>可归因</b>：手点的节奏不可重复，而
/// <see cref="PageSwitchPerfProbe"/> 与 <see cref="UiStallProbe"/> 的两组数只有在
/// 同一段固定节奏下才好对照。每次跳页前打一行标记，日志因此能按页归因。
///
/// 默认关闭，置环境变量 <c>UIHARU_PAGE_SWITCH_BENCH=1</c> 开启。
/// </summary>
public static class PageSwitchBench
{
    private const string EnableVariable = "UIHARU_PAGE_SWITCH_BENCH";

    /// <summary>跳页间隔。留够一页把切换后续的几帧跑完,否则两页的开销会叠在一起</summary>
    private const int IntervalMs = 1200;

    private static readonly MenuPages[] Cycle =
    [
        // 对话页已合并：两类型同页，压测只跳一次
        MenuPages.MenuConversationKey,
        MenuPages.MenuCharacterKey,
        MenuPages.MenuModelKey,
        // MenuPages.MenuServicesKey,
        MenuPages.MenuLogKey,
    ];

    private static DispatcherTimer? _timer;
    private static MainViewModel? _viewModel;
    private static int _index;

    /// <summary>压测是否开启</summary>
    public static bool IsEnabled { get; } =
        Environment.GetEnvironmentVariable(EnableVariable) == "1";

    /// <summary>
    /// 开始轮着跳页。重复调用无副作用
    /// </summary>
    /// <param name="viewModel">主视图模型,跳页经它进行</param>
    public static void Start(MainViewModel viewModel)
    {
        if (!IsEnabled || _timer != null) return;

        _viewModel = viewModel;
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(IntervalMs), DispatcherPriority.Background, OnTick);
        _timer.Start();
        Log.Debug($"[bench] page switch bench on, every {IntervalMs}ms");
    }

    private static void OnTick(object? sender, EventArgs e)
    {
        if (_viewModel == null) return;

        MenuPages key = Cycle[_index++ % Cycle.Length];
        Log.Debug($"[bench] -> {key}");
        _viewModel.JumpToPage(key);
    }
}
