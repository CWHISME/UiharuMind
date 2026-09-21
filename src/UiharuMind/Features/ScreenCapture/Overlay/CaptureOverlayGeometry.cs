using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.ScreenCapture.Overlay;

/// <summary>
/// 遮罩窗的几何与显形：把窗口铺满目标屏，并等原生 frame 真长到位之后才让它现形。
/// 与选区、放大镜等内容逻辑无关，单独拆出来是因为这里全是平台时序的坑，改动频率与内容逻辑完全不同。
/// </summary>
internal sealed class CaptureOverlayGeometry
{
    private readonly Window _window;
    private DispatcherTimer? _revealTimer;
    private int _revealSteadyFrames;
    private int _revealTicks;

    public CaptureOverlayGeometry(Window window)
    {
        _window = window;
    }

    /// <summary>
    /// 把窗口铺满目标屏。<b>必须在 Show 之后调用</b>：Show 之前设尺寸会被 native 按 visibleFrame 裁掉，
    /// 遮罩一生下来就小于全屏。
    /// </summary>
    /// <param name="screen">目标屏幕</param>
    /// <returns>落定的窗口尺寸（DIP）</returns>
    public Size ApplyTo(Screen screen)
    {
        var bounds = screen.Bounds;
        var size = new Size(bounds.Width / screen.Scaling, bounds.Height / screen.Scaling);
        // 原子提交：位置与尺寸分开设会逐格撕裂闪烁
        if (!_window.TrySetWindowFrame(bounds.Position, size)) _window.Position = bounds.Position;
        _window.Width = size.Width;
        _window.Height = size.Height;
        return size;
    }

    /// <summary>
    /// 轮询等原生 frame 真正长到目标尺寸后显形。定时猜（比如 50ms）极易在半路提前打开，
    /// 看到的就是从小撑大加横向撕裂：布局 → ClientSize → setContentSize 是异步链。
    /// ClientSize 到位即布局已出，原生调用是同步跟下来的，再稳两帧给合成器呈现，必不闪；
    /// 30 拍（约半秒）还没好就直接放行，不能一直藏着。
    /// </summary>
    /// <param name="target">目标尺寸（DIP）</param>
    /// <param name="isStillValid">遮罩是否还该显形，返回 false 则放弃本次显形</param>
    /// <param name="reveal">显形动作</param>
    public void ScheduleReveal(Size target, Func<bool> isStillValid, Action reveal)
    {
        Stop();
        _revealSteadyFrames = 0;
        _revealTicks = 0;
        _revealTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _revealTimer.Tick += (_, _) =>
        {
            _revealTicks++;
            if (!isStillValid())
            {
                Stop();
                return;
            }

            if (Math.Abs(_window.ClientSize.Width - target.Width) < 1.0 &&
                Math.Abs(_window.ClientSize.Height - target.Height) < 1.0)
                _revealSteadyFrames++;
            else
                _revealSteadyFrames = 0;

            if (_revealSteadyFrames < 2 && _revealTicks < 30) return;

            reveal();
            Stop();
        };
        _revealTimer.Start();
    }

    public void Stop()
    {
        if (_revealTimer == null) return;
        _revealTimer.Stop();
        _revealTimer = null;
    }
}
