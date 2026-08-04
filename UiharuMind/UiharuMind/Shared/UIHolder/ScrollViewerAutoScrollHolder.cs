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
using Avalonia.Controls;
using Avalonia.Input;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Shared.UIHolder;

/// <summary>
/// 自动滚动的 ScrollViewer 容器至底部(用户操作后取消)
/// </summary>
public class ScrollViewerAutoScrollHolder
{
    private readonly ScrollViewer _scrollViewer;
    private bool _isAutoScrolling = true;

    public ScrollViewerAutoScrollHolder(ScrollViewer scrollViewer)
    {
        _scrollViewer = scrollViewer;
        scrollViewer.ScrollToEnd();
        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    /// <summary>
    /// 恢复自动跟底。内容整体重建(如切换会话)时 Offset 回零会被误读为用户上滚,
    /// 由调用方在集合 Reset 后显式恢复。
    /// </summary>
    public void Resume()
    {
        _isAutoScrolling = true;
        _scrollViewer.ScrollToEnd();
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _isAutoScrolling = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = e.Source as ScrollViewer;
        if (scrollViewer == null) return;

        // 只有内容高度未收缩时的位移才视为用户滚动;
        // 内容重建/清空(切会话)会把 Offset 钳回去,那不是用户意图,误判会让新会话停在顶部
        if (e.OffsetDelta.Y != 0 && e.ExtentDelta.Y >= 0)
        {
            _isAutoScrolling = false;
        }

        if (e.ViewportDelta.Y == 0 && scrollViewer.ScrollBarMaximum.Y > 0 &&
            scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - Math.Max(0, e.ExtentDelta.Y))
        {
            // 有进度条,且用户手动或自动滚动到了底部,继续自动滚动
            _isAutoScrolling = true;
        }

        // 如果需要自动滚动到底部
        if (_isAutoScrolling)
        {
            scrollViewer.ScrollToEnd();
        }
    }
}