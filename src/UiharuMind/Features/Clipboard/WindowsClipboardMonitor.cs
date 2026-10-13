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
using System.Runtime.InteropServices;
using System.Threading;

namespace UiharuMind.Features.Clipboard;

/// <summary>
/// 需要通过创建一个隐藏的窗口并使用 Win32 API 来监听剪贴板的变化
/// 据说是因为 Windows 的剪贴板机制依赖于窗口消息传递系统，而 Avalonia 并不直接提供这种级别的系统集成
/// </summary>
public class WindowsClipboardMonitor : IClipboardMonitor
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(1);

    private readonly Thread _monitorThread;
    private readonly ManualResetEventSlim _windowReady = new(); //窗口在监控线程上创建，建好前没处投递退出消息
    private IntPtr _windowHandle;
    private volatile bool _running = true;
    private bool _disposed;
    private WndProcDelegate _wndProcDelegate = null!;

    public event Action? OnClipboardChanged;

    public WindowsClipboardMonitor()
    {
        _monitorThread = new Thread(RunMessageLoop) { IsBackground = true };
#pragma warning disable CA1416
        _monitorThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        _monitorThread.Start();
    }

    private void RunMessageLoop()
    {
        WndClassEx wndClass = new WndClassEx
        {
            cbSize = Marshal.SizeOf<WndClassEx>()
        };
        _wndProcDelegate = WindowProc;
        wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        wndClass.hInstance = GetModuleHandle(null);
        wndClass.lpszClassName = "ClipboardMonitorWindow";

        ushort classAtom = RegisterClassEx(ref wndClass);

        _windowHandle = CreateWindowEx(0, classAtom, "ClipboardMonitorWindow", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        AddClipboardFormatListener(_windowHandle);
        _windowReady.Set();

        while (_running && GetMessage(out var msg, IntPtr.Zero, 0, 0))
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        RemoveClipboardFormatListener(_windowHandle);
        DestroyWindow(_windowHandle);
    }

    private IntPtr WindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE && _running)
        {
            OnClipboardChanged?.Invoke();
        }
        else if (msg == WM_CLOSE)
        {
            PostQuitMessage(0);
            return IntPtr.Zero;
        }

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        OnClipboardChanged = null;
        // 消息窗口收到 WM_CLOSE 后投递 WM_QUIT，让 GetMessage 返回、循环退出。
        // 句柄为空时 PostMessage 会投到调用线程自己的队列，所以必须先确认窗口建好了
        if (_windowReady.Wait(DisposeTimeout) && _windowHandle != IntPtr.Zero)
        {
            PostMessage(_windowHandle, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        // 超时说明线程还没走完，它之后还可能 Set，此时不能释放
        if (_monitorThread.Join(DisposeTimeout)) _windowReady.Dispose();
        GC.SuppressFinalize(this);
    }

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
    private const int WM_CLOSE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr CreateWindowEx(int dwExStyle, ushort regResult, string lpWindowName, uint dwStyle,
        int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu, IntPtr hInstance, IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern void PostQuitMessage(int nExitCode);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WndClassEx lpwcx);

    [StructLayout(LayoutKind.Sequential)]
    private struct WndClassEx
    {
        public int cbSize;
        public uint style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public Point pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int x;
        public int y;
    }

    private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
