/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace UiharuMind.ViewModels.UIHolder;

/// <summary>
/// 虚拟化消息列表的自动跟底器。
/// 虚拟化下 Extent 是估算值、面板为保持锚点会自发调整 Offset，
/// 不能像 ScrollViewerAutoScrollHolder 那样用 OffsetDelta 推断用户意图——
/// 只从输入事件（滚轮上滚、拖滚动条离底）判定停止跟随，回到底部即恢复；
/// 跟随时用 ScrollIntoView(末项) + ScrollToEnd 把估算的底钉成精确的底。
/// </summary>
public class VirtualizedAutoScrollHolder
{
    private const double BottomThreshold = 16; //离底距离小于该值视为在底部

    private readonly ItemsControl _itemsControl;
    private readonly ScrollViewer _scrollViewer;
    private bool _isFollowing = true; //是否跟随底部
    private bool _isScrollScheduled; //已排队一次钉底,合并连续触发

    public VirtualizedAutoScrollHolder(ItemsControl itemsControl, ScrollViewer scrollViewer)
    {
        _itemsControl = itemsControl;
        _scrollViewer = scrollViewer;
        _scrollViewer.ScrollChanged += OnScrollChanged;
        // 滚轮必须用隧道路由监听:冒泡阶段事件在模板内部的 ScrollContentPresenter
        // 就被标记 Handled,实例订阅(PointerWheelChanged +=)永远收不到——
        // 感知不到用户上滚,跟底器就会把用户滚上去的位置一次次钉回底部
        _scrollViewer.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
            RoutingStrategies.Tunnel);
        // 触控板平移是独立的手势事件,不走滚轮;其 Delta 符号与滚轮相反(正值向下)
        _scrollViewer.AddHandler(InputElement.ScrollGestureEvent, OnScrollGesture,
            RoutingStrategies.Tunnel);
        _scrollViewer.TemplateApplied += OnScrollViewerTemplateApplied;
        _itemsControl.ItemsView.CollectionChanged += OnItemsChanged;
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // 切会话是同一 UI 批次内的 Clear+全量 Add,布局层看不到"空集合"瞬间,
        // 只能靠 Reset 事件把上一会话遗留的"已停跟随"复位
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            _isFollowing = true;
        }

        if (_isFollowing) ScheduleScrollToBottom();
    }

    private void OnScrollGesture(object? sender, ScrollGestureEventArgs e)
    {
        if (e.Delta.Y < 0) _isFollowing = false;
    }

    private void OnScrollViewerTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        // 拖动滚动条不产生滚轮事件,需要单独监听;找不到就退化为只认滚轮
        if (e.NameScope.Find<ScrollBar>("PART_VerticalScrollBar") is { } bar)
        {
            bar.Scroll += OnScrollBarScroll;
        }
    }

    private void OnScrollBarScroll(object? sender, ScrollEventArgs e)
    {
        // 拖动先视为离底,若拖回了底部由 OnScrollChanged 的几何判定恢复
        _isFollowing = false;
    }

    private void OnPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y > 0) _isFollowing = false;
    }

    private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
    {
        if (_itemsControl.ItemCount == 0)
        {
            // 内容清空(切会话)时重置为跟随
            _isFollowing = true;
            return;
        }

        // 已在底部就不再钉底:估算 Extent 会在布局间抖动,
        // 用"offset 是否等于 max"判底会与钉底动作互相触发成死循环
        if (IsAtBottom())
        {
            _isFollowing = true;
            return;
        }

        if (_isFollowing) ScheduleScrollToBottom();
    }

    /// <summary>
    /// 是否在底部:以末项容器的几何位置为准。
    /// 虚拟化下 Extent/ScrollBarMaximum 是估算值且随布局抖动,不能参与判定。
    /// </summary>
    private bool IsAtBottom()
    {
        if (_itemsControl.ContainerFromIndex(_itemsControl.ItemCount - 1) is not { } last) return false;
        var bottom = last.TranslatePoint(new Point(0, last.Bounds.Height), _scrollViewer);
        return bottom is { } point && point.Y <= _scrollViewer.Viewport.Height + BottomThreshold;
    }

    private void ScheduleScrollToBottom()
    {
        if (_isScrollScheduled) return;
        _isScrollScheduled = true;
        Dispatcher.UIThread.Post(() =>
        {
            _isScrollScheduled = false;
            if (!_isFollowing || _itemsControl.ItemCount == 0) return;
            // 先把末项真实布局出来,让底部 Extent 从估算变精确,ScrollToEnd 才能落准
            _itemsControl.ScrollIntoView(_itemsControl.ItemCount - 1);
            _scrollViewer.ScrollToEnd();
        }, DispatcherPriority.Background);
    }
}
