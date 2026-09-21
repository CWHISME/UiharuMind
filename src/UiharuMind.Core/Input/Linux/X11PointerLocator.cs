using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Core.Input.Linux;

/// <summary>
/// 通过 XWayland 的 XQueryPointer 查询全局光标位置。
/// Wayland 协议本身不提供后台查询指针的途径（指针事件只投递给焦点 surface），
/// 因此这是 XWayland 在位时唯一精确且无需特权的来源。XWayland 缺席时构造即失败，
/// 由调用方降级到 UnavailablePointerLocator。
/// </summary>
internal sealed class X11PointerLocator : IPointerLocator
{
    /// 一次 XQueryPointer 是一次 X 服务器往返，而鼠标移动事件可达上千 Hz，
    /// 逐事件查询会把 X 连接打满，这里用极短 TTL 合并同一帧内的重复查询
    private const int CacheLifetimeMilliseconds = 8;

    private readonly object _lock = new();
    private nint _display;
    private nint _rootWindow;
    private short _cachedX;
    private short _cachedY;
    private long _cachedAtTicks;
    private bool _disposed;

    public bool IsAvailable => _display != nint.Zero;

    private X11PointerLocator(nint display, nint rootWindow)
    {
        _display = display;
        _rootWindow = rootWindow;
    }

    /// <summary>
    /// 尝试连接 X 服务器并建立定位器
    /// </summary>
    /// <returns>连接成功返回实例，否则返回 null</returns>
    public static X11PointerLocator? TryCreate()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))) return null;

        try
        {
            var display = XOpenDisplay(null);
            if (display == nint.Zero) return null;

            var root = XDefaultRootWindow(display);
            return new X11PointerLocator(display, root);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
    }

    public bool TryGetPosition(out short x, out short y)
    {
        x = 0;
        y = 0;
        lock (_lock)
        {
            if (_disposed || _display == nint.Zero) return false;

            var now = Environment.TickCount64;
            if (now - _cachedAtTicks < CacheLifetimeMilliseconds)
            {
                x = _cachedX;
                y = _cachedY;
                return true;
            }

            try
            {
                if (XQueryPointer(_display, _rootWindow, out _, out _, out int rootX, out int rootY,
                        out _, out _, out _) == 0)
                {
                    // 光标不在本 X 屏上（多 X 屏场景），保留上一次结果而不是给出假坐标
                    return false;
                }

                _cachedX = (short)Math.Clamp(rootX, short.MinValue, short.MaxValue);
                _cachedY = (short)Math.Clamp(rootY, short.MinValue, short.MaxValue);
                _cachedAtTicks = now;
                x = _cachedX;
                y = _cachedY;
                return true;
            }
            catch (Exception e)
            {
                Log.Warning($"XQueryPointer 失败：{e.Message}");
                return false;
            }
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            if (_display == nint.Zero) return;

            try
            {
                XCloseDisplay(_display);
            }
            catch (Exception)
            {
                // 关闭连接失败不影响退出流程
            }

            _display = nint.Zero;
            _rootWindow = nint.Zero;
        }
    }

    [DllImport("libX11.so.6", CharSet = CharSet.Ansi)]
    private static extern nint XOpenDisplay(string? displayName);

    [DllImport("libX11.so.6")]
    private static extern int XCloseDisplay(nint display);

    [DllImport("libX11.so.6")]
    private static extern nint XDefaultRootWindow(nint display);

    [DllImport("libX11.so.6")]
    private static extern int XQueryPointer(nint display, nint window, out nint rootReturn, out nint childReturn,
        out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
}
