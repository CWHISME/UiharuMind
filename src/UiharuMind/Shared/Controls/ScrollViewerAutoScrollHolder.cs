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
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace UiharuMind.Shared.Controls;

/// <summary>
/// 自动跟底的 ScrollViewer 容器（用户上滚后让开，回到底部再接管）。
///
/// <b>不在 <c>ScrollChanged</c> 回调里直接写 Offset。</b>原先那样写是自反的：
/// 写 Offset 又会抛 ScrollChanged，于是流式期间每次内容增长都要绕好几圈，
/// 而每一圈滚动都会给可视树上所有订阅了 <c>EffectiveViewportChanged</c> 的控件
/// （会话列表里每条消息的 markdown 视图都订阅了）各发一次通知——开销正比于条目数。
///
/// 现在的口径：回调只负责<b>判断状态</b>，改 Offset 一律走
/// <see cref="RequestStickToBottom"/> 排队，同一批增长只补一次底。
/// </summary>
public class ScrollViewerAutoScrollHolder
{
    /// <summary>离底多少像素以内算"在底部"。亚像素布局下精确相等不可靠</summary>
    private const double BottomTolerance = 4.0;

    private readonly ScrollViewer _scrollViewer;
    private bool _isStuckToBottom = true;
    private bool _isSyncPending; //已排了一次补底,同一批增长不重复排

    /// <summary>
    /// 此刻是否跟着底部。运行期裁剪据此决定"能不能裁"——用户上滚在读旧消息时裁，
    /// 他正看的内容会当场消失。
    /// </summary>
    public bool IsStuckToBottom => _isStuckToBottom;

    public ScrollViewerAutoScrollHolder(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
        RequestStickToBottom();
    }

    /// <summary>
    /// 恢复自动跟底。内容整体重建(如切换会话)时由调用方在集合 Reset 后显式恢复。
    /// </summary>
    public void Resume()
    {
        _isStuckToBottom = true;
        RequestStickToBottom();
    }

    /// <summary>
    /// 请求补一次底。已在跟底状态才生效；同一批请求合并成一次实际滚动。
    /// </summary>
    public void RequestStickToBottom()
    {
        if (!_isStuckToBottom || _isSyncPending) return;

        _isSyncPending = true;
        // Loaded(1) 的语义是"布局与渲染之后、输入之前"(见 Avalonia 的 DispatcherPriority 文档),
        // 此刻 Extent 已经是新内容的值,补底不会补到旧几何上
        Dispatcher.UIThread.Post(SyncToBottom, DispatcherPriority.Loaded);
    }

    private void SyncToBottom()
    {
        _isSyncPending = false;
        if (!_isStuckToBottom || IsAtBottom) return;

        _scrollViewer.Offset = new Vector(_scrollViewer.Offset.X, MaxOffset);
    }

    /// <summary>滚轮是用户意图里最明确的一个,不等 ScrollChanged 的几何判断,立刻让开</summary>
    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0) _isStuckToBottom = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        // 内容长高了:这不是用户意图,而是要补底的信号。
        // 此处不能按"离底距离"重判状态——流式期间 extent 一直在长,
        // 每次增长都会让离底距离变大,照那个判据跟随会被自己关掉
        if (e.ExtentDelta.Y > 0)
        {
            RequestStickToBottom();
            return;
        }

        // 其余情形(用户滚动、拖动进度条、视口变化、内容收缩)一律按几何重判:
        // 在底部就接管，离开底部就让开。补底本身也走这里，它落在底部,结论仍是接管
        _isStuckToBottom = IsAtBottom;
    }

    private double MaxOffset => Math.Max(0, _scrollViewer.Extent.Height - _scrollViewer.Viewport.Height);

    private bool IsAtBottom => MaxOffset - _scrollViewer.Offset.Y <= BottomTolerance;
}
