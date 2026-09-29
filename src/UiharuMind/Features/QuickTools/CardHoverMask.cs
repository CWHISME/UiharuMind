using System;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using SharpHook.Data;
using UiharuMind.Core.Input;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;

namespace UiharuMind.Features.QuickTools;

/// <summary>
/// 窗口比可见卡片大时，用全局鼠标位置判断光标是否在卡片上：
/// 在卡片外让窗口点击穿透，透明区域就不会成为挡点击的幽灵区；
/// 进入/离开卡片通过回调交给窗口做展开/收起。
/// 之所以不用 Avalonia 的 PointerEntered/Exited：窗口忽略鼠标事件后它收不到 Exited。
/// </summary>
internal sealed class CardHoverMask : IDisposable
{
    private readonly Window _window;
    private readonly Func<Size> _cardSize;
    private readonly Action<bool> _onHoverChanged;
    private int _refreshPending;
    private bool _started;

    public CardHoverMask(Window window, Func<Size> cardSize, Action<bool> onHoverChanged)
    {
        _window = window;
        _cardSize = cardSize;
        _onHoverChanged = onHoverChanged;
    }

    /// <summary>光标此刻是否在卡片上</summary>
    public bool IsInside { get; private set; }

    /// <summary>
    /// 开始跟踪。窗口每次显示时调用
    /// </summary>
    public void Start()
    {
        if (_started) return;
        _started = true;
        InputManager.Instance.EventOnMouseMoved += OnMouseMoved;
        IsInside = false;
        OverlayWindowService.TrySetNativeMouseEventsIgnored(_window, true);
        Refresh();
    }

    public void Dispose()
    {
        if (!_started) return;
        _started = false;
        InputManager.Instance.EventOnMouseMoved -= OnMouseMoved;
    }

    // 全局钩子线程上来的，合并成 UI 线程上的一次刷新，鼠标狂动时不堆积
    private void OnMouseMoved(MouseEventData _)
    {
        if (Interlocked.Exchange(ref _refreshPending, 1) == 0)
            Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Input);
    }

    /// <summary>
    /// 按当前光标位置刷新穿透状态，进出卡片时触发回调
    /// </summary>
    public void Refresh()
    {
        Interlocked.Exchange(ref _refreshPending, 0);
        if (!_started) return;

        bool inside = UiUtils.IsMouseInRange(_window.Position, _cardSize());
        if (inside == IsInside) return;

        IsInside = inside;
        OverlayWindowService.TrySetNativeMouseEventsIgnored(_window, !inside);
        _onHoverChanged(inside);
    }
}
