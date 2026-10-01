/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Threading;

namespace UiharuMind.Features.Conversation;

/// <summary>
/// 会话流滚动条滑块的拖动态：按住期间按固定间隔回调一次，松手时回调一次。
///
/// 只认会话流自己那根滑块——卡片里嵌着的滚动容器（工具结果、diff）的滑块事件也会冒泡到这里。
/// 按住不动时滚动事件一个都不会来（Offset 被钳在 0 上不变），所以「停在顶部连续翻」只能靠定时器驱动。
/// </summary>
internal sealed class ViewerThumbDrag
{
    /// <summary>按住期间两次回调的间隔：够快到能一路翻回去，又给每次前插后的布局留足喘息</summary>
    private static readonly TimeSpan HeldInterval = TimeSpan.FromMilliseconds(300);

    private readonly ScrollViewer _viewer;
    private readonly Action _released;
    private readonly DispatcherTimer _heldTimer;

    /// <summary>是否正按住会话流自己的滑块</summary>
    public bool IsDragging { get; private set; }

    /// <summary>
    /// 构造并挂上滑块的拖动事件
    /// </summary>
    /// <param name="viewer">会话流的滚动容器</param>
    /// <param name="whileHeld">按住期间每隔一段时间调用一次</param>
    /// <param name="released">松手时调用</param>
    public ViewerThumbDrag(ScrollViewer viewer, Action whileHeld, Action released)
    {
        _viewer = viewer;
        _released = released;
        // 不能用带回调的那个构造:它构造即启动——每个视图从建出来起就每 300ms 空跑一次,
        // 而在跑的计时器被调度器强引用着,视图连同它的数据上下文就再也回收不掉
        _heldTimer = new DispatcherTimer(HeldInterval, DispatcherPriority.Background, Dispatcher.UIThread);
        _heldTimer.Tick += (_, _) => whileHeld();
        viewer.AddHandler(Thumb.DragStartedEvent, OnDragStarted);
        viewer.AddHandler(Thumb.DragCompletedEvent, OnDragCompleted);
    }

    private void OnDragStarted(object? sender, VectorEventArgs e)
    {
        if (!IsOwnThumb(e.Source)) return;

        IsDragging = true;
        _heldTimer.Start();
    }

    private void OnDragCompleted(object? sender, VectorEventArgs e)
    {
        if (!IsOwnThumb(e.Source)) return;

        IsDragging = false;
        _heldTimer.Stop();
        _released();
    }

    private bool IsOwnThumb(object? source) =>
        source is Thumb { TemplatedParent: ScrollBar { TemplatedParent: var owner } } && ReferenceEquals(owner, _viewer);
}
