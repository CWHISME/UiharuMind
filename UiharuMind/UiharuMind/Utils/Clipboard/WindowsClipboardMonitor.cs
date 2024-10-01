using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace UiharuMind.Utils.Clipboard;

/// <summary>
/// 需要通过创建一个隐藏的窗口并使用 Win32 API 来监听剪贴板的变化
/// 据说是因为 Windows 的剪贴板机制依赖于窗口消息传递系统，而 Avalonia 并不直接提供这种级别的系统集成
/// </summary>
public class WindowsClipboardMonitor : IClipboardMonitor
{
    private readonly Thread _monitorThread;
    private IntPtr _windowHandle;
    private bool _running;
    private WndProcDelegate _wndProcDelegate;

    public event Action? OnClipboardChanged;

    public WindowsClipboardMonitor()
    {
        _monitorThread = new Thread(RunMessageLoop);
#pragma warning disable CA1416
        _monitorThread.SetApartmentState(ApartmentState.STA);
#pragma warning restore CA1416
        _monitorThread.Start();
    }

    private void RunMessageLoop()
    {
        _running = true;

        WndClassEx wndClass = new WndClassEx
        {
            cbSize = Marshal.SizeOf(typeof(WndClassEx))
        };
        _wndProcDelegate = WindowProc;
        wndClass.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
        wndClass.hInstance = Marshal.GetHINSTANCE(GetType().Module);
        wndClass.lpszClassName = "ClipboardMonitorWindow";

        ushort classAtom = RegisterClassEx(ref wndClass);

        _windowHandle = CreateWindowEx(0, classAtom, "ClipboardMonitorWindow", 0,
            0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);

        AddClipboardFormatListener(_windowHandle);

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

        return DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        _running = false;
        PostThreadMessage(_monitorThread.ManagedThreadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
        // 发送一个剪切板事件，确保 GetMessage 能够接收到这个事件
        PostMessage(_windowHandle, WM_CLIPBOARDUPDATE, IntPtr.Zero, IntPtr.Zero);
        _monitorThread.Join();

        //直接销毁似乎其实也可以
        // DestroyWindow(_windowHandle);
        GC.SuppressFinalize(this);
    }

    ~WindowsClipboardMonitor()
    {
        Dispose();
    }

    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private static readonly IntPtr HWND_MESSAGE = new IntPtr(-3);
    private const int WM_QUIT = 0x0012;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool AddClipboardFormatListener(IntPtr hwnd);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(int idThread, uint msg, IntPtr wParam, IntPtr lParam);

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