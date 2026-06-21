using System.Runtime.InteropServices;
using SharpHook.Data;

namespace UiharuMind.Core.Input;

public sealed class WindowsInputHookBackend : IInputHookBackend
{
    private const int WhMouseLl = 14;
    private const int WmKeyDown = 0x0100;
    private const int WmKeyUp = 0x0101;
    private const int WmSysKeyDown = 0x0104;
    private const int WmSysKeyUp = 0x0105;
    private const int WmInput = 0x00FF;
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
    private const int RidInput = 0x10000003;
    private const int RidevInputSink = 0x00000100;
    private const int RimTypeKeyboard = 1;
    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkMenu = 0x12;
    private const int VkLeftShift = 0xA0;
    private const int VkRightShift = 0xA1;
    private const int VkLeftControl = 0xA2;
    private const int VkRightControl = 0xA3;
    private const int VkLeftMenu = 0xA4;
    private const int VkRightMenu = 0xA5;
    private const uint RiKeyBreak = 0x0001;
    private const uint RiKeyE0 = 0x0002;
    private const uint MapvkVscToVkEx = 3;
    private const string RawInputWindowClassName = "UiharuMindRawInputMessageWindow";
    private static readonly IntPtr HwndMessage = new(-3);

    private readonly LowLevelProc _mouseProc;
    private readonly WndProc _wndProc;
    private readonly HashSet<MouseButton> _pressedButtons = new();

    private IntPtr _mouseHook;
    private IntPtr _messageWindow;
    private uint _hookThreadId;
    private bool _disposed;

    public WindowsInputHookBackend()
    {
        _mouseProc = MouseHookCallback;
        _wndProc = RawInputWindowProc;
    }

    public bool IsRunning => _messageWindow != IntPtr.Zero || _mouseHook != IntPtr.Zero;

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
        _messageWindow = CreateRawInputMessageWindow();
        RegisterKeyboardRawInput(_messageWindow);
        _mouseHook = SetWindowsHookEx(WhMouseLl, _mouseProc, IntPtr.Zero, 0);

        if (_messageWindow == IntPtr.Zero || _mouseHook == IntPtr.Zero)
        {
            DisposeHooks();
            throw new InvalidOperationException(
                $"Failed to initialize Windows input adapter. Win32Error={Marshal.GetLastWin32Error()}");
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

    private IntPtr RawInputWindowProc(IntPtr hwnd, uint message, UIntPtr wParam, IntPtr lParam)
    {
        if (message == WmInput)
        {
            ProcessRawKeyboardInput(lParam);
        }

        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private void ProcessRawKeyboardInput(IntPtr rawInputHandle)
    {
        uint size = 0;
        var headerSize = (uint)Marshal.SizeOf<RawInputHeader>();
        GetRawInputData(rawInputHandle, RidInput, IntPtr.Zero, ref size, headerSize);
        if (size == 0) return;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(rawInputHandle, RidInput, buffer, ref size, headerSize) != size) return;

            var rawInput = Marshal.PtrToStructure<RawInput>(buffer);
            if (rawInput.Header.Type != RimTypeKeyboard) return;

            var virtualKey = NormalizeRawVirtualKey(rawInput.Keyboard);
            var keyCode = InputKeyCodeMapper.FromWindowsVirtualKey(virtualKey);
            if (keyCode == KeyCode.VcUndefined) return;

            var isKeyUp = (rawInput.Keyboard.Flags & RiKeyBreak) != 0;
            if (isKeyUp)
            {
                KeyReleased?.Invoke(keyCode);
            }
            else
            {
                KeyPressed?.Invoke(keyCode);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static int NormalizeRawVirtualKey(RawKeyboard keyboard)
    {
        var virtualKey = keyboard.VKey;
        return virtualKey switch
        {
            VkShift => MapVirtualKey(keyboard.MakeCode, MapvkVscToVkEx) == VkRightShift ? VkRightShift : VkLeftShift,
            VkControl => (keyboard.Flags & RiKeyE0) != 0 ? VkRightControl : VkLeftControl,
            VkMenu => (keyboard.Flags & RiKeyE0) != 0 ? VkRightMenu : VkLeftMenu,
            _ => virtualKey
        };
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
        if (_messageWindow != IntPtr.Zero)
        {
            DestroyWindow(_messageWindow);
            _messageWindow = IntPtr.Zero;
        }

        if (_mouseHook != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_mouseHook);
            _mouseHook = IntPtr.Zero;
        }

        _hookThreadId = 0;
        _pressedButtons.Clear();
    }

    private static void RegisterKeyboardRawInput(IntPtr hwnd)
    {
        var devices = new[]
        {
            new RawInputDevice
            {
                UsagePage = 0x01,
                Usage = 0x06,
                Flags = RidevInputSink,
                Target = hwnd
            }
        };

        if (!RegisterRawInputDevices(devices, (uint)devices.Length, (uint)Marshal.SizeOf<RawInputDevice>()))
        {
            throw new InvalidOperationException(
                $"Failed to register Raw Input keyboard device. Win32Error={Marshal.GetLastWin32Error()}");
        }
    }

    private IntPtr CreateRawInputMessageWindow()
    {
        var instance = GetModuleHandle(null);
        var windowClass = new WndClass
        {
            Style = 0,
            WndProc = _wndProc,
            Instance = instance,
            ClassName = RawInputWindowClassName
        };

        RegisterClass(ref windowClass);
        return CreateWindowEx(
            0,
            RawInputWindowClassName,
            RawInputWindowClassName,
            0,
            0,
            0,
            0,
            0,
            HwndMessage,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);
    }

    private delegate IntPtr LowLevelProc(int nCode, IntPtr wParam, IntPtr lParam);
    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
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

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputDevice
    {
        public ushort UsagePage;
        public ushort Usage;
        public uint Flags;
        public IntPtr Target;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInputHeader
    {
        public int Type;
        public int Size;
        public IntPtr Device;
        public IntPtr WParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawKeyboard
    {
        public ushort MakeCode;
        public ushort Flags;
        public ushort Reserved;
        public ushort VKey;
        public uint Message;
        public uint ExtraInformation;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RawInput
    {
        public RawInputHeader Header;
        public RawKeyboard Keyboard;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClass
    {
        public uint Style;
        public WndProc WndProc;
        public int ClsExtra;
        public int WndExtra;
        public IntPtr Instance;
        public IntPtr Icon;
        public IntPtr Cursor;
        public IntPtr Background;
        public string? MenuName;
        public string ClassName;
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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices(
        RawInputDevice[] pRawInputDevices,
        uint uiNumDevices,
        uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(
        IntPtr hRawInput,
        int uiCommand,
        IntPtr pData,
        ref uint pcbSize,
        uint cbSizeHeader);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern ushort RegisterClass(ref WndClass lpWndClass);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int MapVirtualKey(int uCode, uint uMapType);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? lpModuleName);
}
