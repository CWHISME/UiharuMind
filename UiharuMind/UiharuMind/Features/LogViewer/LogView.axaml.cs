/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 ****************************************************************************/

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Features.LogViewer;

/// <summary>
/// 日志面板。选中状态与高亮全部交给 <c>ListBox</c>——
/// 原先那套手写指针命中 + 手改 <c>Border</c> 背景色的做法在虚拟化下会串色：
/// 容器被回收复用时，手动染上的高亮会跟着跑到另一条日志上。
///
/// 这里仍然保留的只有<b>按住拖动改选中</b>：<c>ListBox</c> 原生只在按下那一刻选中，
/// 而扫读日志时经常要按住往下捋。命中到容器后交给 <c>SelectedItem</c>，不碰任何样式。
///
/// ⚠️ 拖动必须挂在<b>隧道阶段</b>。原先那套挂的是 <c>ItemsControl</c>，它不捕获指针；
/// <c>ListBoxItem</c> 会捕获指针并把 <c>PointerMoved</c> 标成已处理，
/// 冒泡阶段的 <c>OnPointerMoved</c> 因此根本不会被调到
/// </summary>
public partial class LogView : UserControl
{
    private bool _isDragging;
    private bool _captureReleased; //本次拖动是否已经放掉 ListBoxItem 的指针捕获

    public LogView()
    {
        InitializeComponent();
        DataContext = App.ViewModel.GetViewModel<LogViewModel>();

        AddHandler(PointerPressedEvent, OnPointerPressedTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnPointerMovedTunnel, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPointerReleasedTunnel, RoutingStrategies.Tunnel);
    }

    private void OnPointerPressedTunnel(object? sender, PointerPressedEventArgs e)
    {
        // 必须限定在列表内起手。否则拖 GridSplitter 时也会走进来,
        // 下面那句放掉捕获会连 GridSplitter 自己的捕获一起抢掉,拉伸当场失效
        // 同样要排除滚动条:ScrollBar 自己会捕获指针,一抢它的捕获,滑块就拖不动了
        _isDragging = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
                      && IsInsideList(e.Source)
                      && !IsOnScrollBar(e.Source);
        _captureReleased = false;
    }

    private bool IsInsideList(object? source) =>
        source is Visual visual && (ReferenceEquals(visual, LogList) ||
                                    visual.FindAncestorOfType<ListBox>() == LogList);

    // 命中滚动条自身或它的任何部件(Thumb/Track/RepeatButton)都算滚动条,让给 ScrollBar 自己处理
    private bool IsOnScrollBar(object? source) =>
        source is Visual visual && visual.FindAncestorOfType<ScrollBar>(includeSelf: true) != null;

    private void OnPointerMovedTunnel(object? sender, PointerEventArgs e)
    {
        if (!_isDragging) return;

        // 在面板外松开时收不到 Released,靠按键状态兜底,否则会一直处于拖动态
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isDragging = false;
            return;
        }

        // 按下的那个 ListBoxItem 捕获了指针,会一直停在「按下」态——看上去就是老的选中没消。
        // 拖动一开始就把捕获放掉,选中完全由我们按指针位置给
        if (!_captureReleased)
        {
            e.Pointer.Capture(null);
            _captureReleased = true;
        }

        SelectItemUnderPointer(e);
    }

    private void OnPointerReleasedTunnel(object? sender, PointerReleasedEventArgs e)
    {
        _isDragging = false;
        _captureReleased = false;
    }

    private void SelectItemUnderPointer(PointerEventArgs e)
    {
        IInputElement? hit = LogList.InputHitTest(e.GetPosition(LogList));
        ListBoxItem? container = (hit as Visual)?.FindAncestorOfType<ListBoxItem>(includeSelf: true);
        if (container?.DataContext is not LogIndexEntry entry) return;
        if (!ReferenceEquals(LogList.SelectedItem, entry)) LogList.SelectedItem = entry;
    }
}
