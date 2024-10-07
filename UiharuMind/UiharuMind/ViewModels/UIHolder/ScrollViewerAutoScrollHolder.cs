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

using Avalonia.Controls;
using Avalonia.Input;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.ViewModels.UIHolder;

/// <summary>
/// 自动滚动的 ScrollViewer 容器至底部(用户操作后取消)
/// </summary>
public class ScrollViewerAutoScrollHolder
{
    private bool _isAutoScrolling = true;

    public ScrollViewerAutoScrollHolder(ScrollViewer scrollViewer)
    {
        scrollViewer.ScrollToEnd();
        scrollViewer.ScrollChanged += OnScrollChanged;
        scrollViewer.PointerWheelChanged += OnPointerWheelChanged;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        _isAutoScrolling = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        var scrollViewer = e.Source as ScrollViewer;
        if (scrollViewer == null) return;
        // bool isManualScroll = e.ExtentDelta.Y == 0;
        if (e.OffsetDelta.Y > 0)
        {
            // 用户向下滚动
            _isAutoScrolling = false;
        }
        else if (e.OffsetDelta.Y < 0)
        {
            // 用户向上滚动
            _isAutoScrolling = false;
        }
        else if (e.ViewportDelta.Y == 0 && scrollViewer.ScrollBarMaximum.Y > 0 &&
                 scrollViewer.Offset.Y >= scrollViewer.ScrollBarMaximum.Y - e.ExtentDelta.Y)
        {
            // 有进度条，且用户手动或自动滚动到了底部，继续自动滚动
            _isAutoScrolling = true;
        }

        // 如果需要自动滚动到底部
        if (_isAutoScrolling)
        {
            scrollViewer.ScrollToEnd();
        }

        // Log.Debug(
        //     $"Scroll changed: ExtentHeight={e.ExtentDelta}, ViewportHeight={e.OffsetDelta}, VerticalOffset={e.ViewportDelta}");
    }
}