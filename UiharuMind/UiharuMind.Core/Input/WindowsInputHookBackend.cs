using System.Runtime.InteropServices;
using SharpHook.Data;

namespace UiharuMind.Core.Input;

public sealed class WindowsInputHookBackend : IInputHookBackend
{
    private const int WhKeyboardLl = 13;
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmMouseMove = 0x0200;
    private const int WmLButtonDown = 0x0201;
    private const int WmLButtonUp = 0x0202;
    private const int WmRButtonDown = 0x0204;
    private const int WmRButtonUp = 0x0205;
    private const int WmMButtonDown = 0x0207;
    private const int WmMButtonUp = 0x0208;
    private const int WmMouseWheel = 0x020A;
    private const int WmXButtonDown = 0x020B;
    private const int WmXButtonUp = 0x020C;
    private const int WmMouseHWheel = 0x020E;
    private const int WmQuit = 0x0012;

    private readonly LowLevelProc _keyboardProc;
    private readonly LowLevelProc _mouseProc;
    private readonly HashSet<MouseButton> _pressedButtons = new();

    private IntPtr _keyboardHook;
    private IntPtr _mouseHook;
    private uint _hookThreadId;
    private bool _disposed;

    public WindowsInputHookBackend()
    {
        _keyboardProc = KeyboardHookCallback;
        _mouseProc = MouseHookCallback;
    }

    public bool IsRunning => _keyboardHook != IntPtr.Zero || _mouseHook != IntPtr.Zero;

    public bool? IsKeyPressed(KeyCode keyCode)
    {
        var virtualKey = InputKeyCodeMapper.ToWindowsVirtualKey(keyCode);
        return virtualKey == 0 ? null : (GetAsyncKeyState(virtualKey) & 0x8000) != 0;
    }

    public event Action? HookEnabled;
    public event Action? HookDisabled;
    public event Func<KeyCode, bool>? KeyPressed;
    public event Action<KeyCode>? KeyReleased;
    public event Action<MouseEventData>? MousePressed;
    public event Action<MouseEventData>? MouseReleased;
    public event Action<MouseEventData>? MouseMoved;
    public event Action<MouseEventData>? MouseDragged;
    public event Action<MouseWheelEventData>? MouseWheel;

    public Task RunAsync()
    {
        return Task.Run(RunMessageLoop);
    }

    public void Dispose()
    {
        _disposed = true;
        if (_hookThreadId != 0)
        {
            PostThreadMessage(_hookThreadId, WmQuit, UIntPtr.Zero, IntPtr.Zero);
        }
    }

    private void RunMessageLoop()
    {
        _disposed = false;
        _hookThreadId = GetCurrentThreadId();
        _keyboardHook = SetWindowsHookEx(WhKeyboardLl, _keyboardProc, IntPtr.Zero, 0);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, IntPtr.Zero, 0);

        if (_keyboardHook == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            DisposeHooks();
            throw new InvalidOperationException(
                $"Failed to install Windows input hooks. Win32Error={Marshal.GetLastWin32Error()}");
        }

        HookEnabled?.Invoke();

        try
        {
            while (!_disposed && GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }
        }
        finally
        {
            DisposeHooks();
            HookDisabled?.Invoke();
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<KbdLlHookStruct>(lParam);
        var keyCode = InputKeyCodeMapper.FromWindowsVirtualKey((int)data.VkCode);
        if (keyCode == KeyCode.VcUndefined)
        {
            return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
        }

        var message = wParam.ToInt32();
        var suppress = false;
        if (message is WmKeyDown or WmSysKeyDown)
        {
            suppress = KeyPressed?.Invoke(keyCode) == true;
        }
        else if (message is WmKeyUp or WmSysKeyUp)
        {
            KeyReleased?.Invoke(keyCode);
        }

        return suppress ? 1 : CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
        {
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        }

        var data = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
        var message = wParam.ToInt32();
        var mouseData = CreateMouseEventData(data, GetMouseButton(message, data.MouseData));

        switch (message)
        {
            case WmMouseMove:
                if (_pressedButtons.Count > 0) MouseDragged?.Invoke(mouseData);
                else MouseMoved?.Invoke(mouseData);
                break;
            case WmLButtonDown:
            case WmRButtonDown:
            case WmMButtonDown:
            case WmXButtonDown:
                _pressedButtons.Add(mouseData.Button);
                MousePressed?.Invoke(mouseData);
                break;
            case WmLButtonUp:
            case WmRButtonUp:
            case WmMButtonUp:
            case WmXButtonUp:
                _pressedButtons.Remove(mouseData.Button);
                MouseReleased?.Invoke(mouseData);
                break;
            case WmMouseWheel:
            case WmMouseHWheel:
                MouseWheel?.Invoke(CreateMouseWheelEventData(data, message == WmMouseHWheel));
                break;
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private static MouseEventData CreateMouseEventData(MsLlHookStruct data, MouseButton button)
    {
        return new MouseEventData
        {
            Button = button,
            Clicks = 1,
            X = ToShort(data.Point.X),
            Y = ToShort(data.Point.Y)
        };
    }

    private static MouseWheelEventData CreateMouseWheelEventData(MsLlHookStruct data, bool horizontal)
    {
        var rotation = (short)((data.MouseData >> 16) & 0xffff);
        return new MouseWheelEventData
        {
            X = ToShort(data.Point.X),
            Y = ToShort(data.Point.Y),
            Type = MouseWheelScrollType.UnitScroll,
            Rotation = rotation,
            Delta = 120,
            Direction = horizontal ? MouseWheelScrollDirection.Horizontal : MouseWheelScrollDirection.Vertical
        };
    }

    private static MouseButton GetMouseButton(int message, uint mouseData)
    {
        return message switch
        {
            WmLButtonDown or WmLButtonUp => MouseButton.Button1,
            WmRButtonDown or WmRButtonUp => MouseButton.Button2,
            WmMButtonDown or WmMButtonUp => MouseButton.Button3,
            WmXButtonDown or WmXButtonUp => ((mouseData >> 16) & 0xffff) == 2 ? MouseButton.Button5 : MouseButton.Button4,
            _ => MouseButton.NoButton
        };
    }

    private static short ToShort(int value)
    {
        return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    private void DisposeHooks()
    {
        if (_keyboardHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_keyboardHook);
            _keyboardHook = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _hookThreadId = 0;
        _pressedButtons.Clear();
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KbdLlHookStruct
    {
        public uint VkCode;
        public uint ScanCode;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct
    {
        public Point Point;
        public uint MouseData;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public Point Pt;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
}
