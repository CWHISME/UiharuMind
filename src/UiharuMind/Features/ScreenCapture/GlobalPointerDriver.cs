using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SharpHook.Data;
using UiharuMind.Core.Input;
using UiharuMind.Shared.Utils;
using UIDispatcher = Avalonia.Threading.Dispatcher;

namespace UiharuMind.Features.ScreenCapture;

/// <summary>
/// 全局指针补位驱动（组合进遮罩窗使用，而非继承）：窗口收不到的按下
/// （mac 菜单栏顶边、被菜单标题拦截的点击等）由全局钩子补上。
/// 只做输入源，不管选区逻辑；映射落到窗外直接丢弃，绝不引入错误坐标。
/// <b>纯补位</b>：窗口自己收得到事件时，调用方应无视这里的移动与松开——
/// 同一次拖动被两个来源交替刷新，哪怕坐标只差一点点，选区与 tips 也会一直抖。
/// </summary>
public sealed class GlobalPointerDriver : IDisposable
{
    private readonly Window _window;
    private readonly Func<Screen?> _currentScreen;
    private bool _disposed;

    /// <summary>左键按下：窗内 DIP 坐标，屏幕坐标系坐标（与 Screen.Bounds 同口径）</summary>
    public event Action<Point, PixelPoint>? Pressed;

    /// <summary>指针移动：同上。调用方自行决定是否处于选区中</summary>
    public event Action<Point, PixelPoint>? Moved;

    /// <summary>
    /// 左键松开：屏幕坐标系坐标。<b>落在窗外也会报</b>——松手是一次框选的终结事件，
    /// 拖到别的屏上松手时窗口收不到 mouseUp（鼠标已经在别的应用上面），
    /// 这里再不补位遮罩就一直挂着，会话守卫会把之后所有截图都挡掉
    /// </summary>
    public event Action<PixelPoint>? Released;

    /// <summary>纯右键按下：调用方按取消处理</summary>
    public event Action? RightPressed;

    public GlobalPointerDriver(Window window, Func<Screen?> currentScreen)
    {
        _window = window;
        _currentScreen = currentScreen;
        InputManager.Instance.EventOnMousePressed += OnHookPressed;
        InputManager.Instance.EventOnMouseReleased += OnHookReleased;
        InputManager.Instance.EventOnMouseMoved += OnHookMoved;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        InputManager.Instance.EventOnMousePressed -= OnHookPressed;
        InputManager.Instance.EventOnMouseReleased -= OnHookReleased;
        InputManager.Instance.EventOnMouseMoved -= OnHookMoved;
    }

    // 钩子线程回调，只做转发，逻辑在 UI 线程执行
    private void OnHookPressed(MouseEventData data)
    {
        if (data.Button != MouseButton.Button1 && data.Button != MouseButton.Button2) return;
        int px = data.X;
        int py = data.Y;
        bool isRight = data.Button == MouseButton.Button2;
        UIDispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            // 先映射：落到窗外（别的屏上点的）直接丢弃，右键也不取消
            if (!TryMapToWindow(px, py, out var windowDip, out var screenUnits)) return;
            if (isRight)
            {
                RightPressed?.Invoke();
                return;
            }

            Pressed?.Invoke(windowDip, screenUnits);
        });
    }

    private void OnHookReleased(MouseEventData data)
    {
        if (data.Button != MouseButton.Button1) return;
        int px = data.X;
        int py = data.Y;
        UIDispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed || !_window.IsVisible) return;
            // 不看映射结果：拖出窗外松手同样要收尾，坐标交给调用方自己钳制
            Released?.Invoke(new PixelPoint(px, py));
        });
    }

    private void OnHookMoved(MouseEventData data)
    {
        int px = data.X;
        int py = data.Y;
        UIDispatcher.UIThread.InvokeAsync(() =>
        {
            if (_disposed) return;
            if (TryMapToWindow(px, py, out var windowDip, out var screenUnits))
                Moved?.Invoke(windowDip, screenUnits);
        });
    }

    private bool TryMapToWindow(int px, int py, out Point windowDip, out PixelPoint screenUnits)
    {
        windowDip = default;
        screenUnits = default;
        var screen = _currentScreen();
        if (screen == null || !_window.IsVisible) return false;
        var window = _window;
        return DisplayUnits.TryMapGlobalPointerToWindow(
            px, py,
            DisplayUnits.PositionUnitsPerDip(screen.Scaling, window.RenderScaling),
            window.Position, window.ClientSize,
            out windowDip, out screenUnits);
    }
}
