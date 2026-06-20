using System.Runtime.InteropServices;
using SharpHook.Data;

namespace UiharuMind.Core.Input;

public class WindowsInputSimulatorBackend : IInputSimulatorBackend
{
    private const int InputMouse = 0;
    private const int InputKeyboard = 1;
    private const uint KeyEventFKeyUp = 0x0002;
    private const uint KeyEventFUnicode = 0x0004;
    private const uint MouseEventFMove = 0x0001;
    private const uint MouseEventFLeftDown = 0x0002;
    private const uint MouseEventFLeftUp = 0x0004;
    private const uint MouseEventFRightDown = 0x0008;
    private const uint MouseEventFRightUp = 0x0010;
    private const uint MouseEventFMiddleDown = 0x0020;
    private const uint MouseEventFMiddleUp = 0x0040;
    private const uint MouseEventFWheel = 0x0800;
    private const uint MouseEventFXDown = 0x0080;
    private const uint MouseEventFXUp = 0x0100;
    private const uint MouseEventFAbsolute = 0x8000;
    private const uint MouseEventFVirtualDesk = 0x4000;
    private const int SmXVirtualScreen = 76;
    private const int SmYVirtualScreen = 77;
    private const int SmCxVirtualScreen = 78;
    private const int SmCyVirtualScreen = 79;

    public void SendMouseMove(short x, short y)
    {
        var (normalizedX, normalizedY) = NormalizeAbsolutePoint(x, y);
        SendMouseInput(MouseEventFMove | MouseEventFAbsolute | MouseEventFVirtualDesk, normalizedX, normalizedY);
    }

    public async Task SendMouseClick(MouseButton button, int delayMs = 100)
    {
        SimulateMousePress(button);
        await Task.Delay(delayMs);
        SimulateMouseRelease(button);
    }

    public void SimulateMousePress(MouseButton button)
    {
        var (flags, mouseData) = GetMouseButtonFlags(button, true);
        SendMouseInput(flags, mouseData: mouseData);
    }

    public void SimulateMouseRelease(MouseButton button)
    {
        var (flags, mouseData) = GetMouseButtonFlags(button, false);
        SendMouseInput(flags, mouseData: mouseData);
    }

    public void SimulateMouseWheel(int wheelDelta)
    {
        SendMouseInput(MouseEventFWheel, mouseData: wheelDelta);
    }

    public async Task SendKeyPress(KeyCode keyCode, int delayMs = 100)
    {
        SimulateKeyPress(keyCode);
        await Task.Delay(delayMs);
        SimulateKeyRelease(keyCode);
    }

    public void SimulateKeyPress(KeyCode keyCode)
    {
        SendKeyboardInput(keyCode, false);
    }

    public void SimulateKeyRelease(KeyCode keyCode)
    {
        SendKeyboardInput(keyCode, true);
    }

    public async Task SendText(string text, int delayBetweenKeys = 50)
    {
        foreach (var c in text)
        {
            SendUnicodeChar(c, false);
            SendUnicodeChar(c, true);
            if (delayBetweenKeys > 0) await Task.Delay(delayBetweenKeys);
        }
    }

    private static void SendKeyboardInput(KeyCode keyCode, bool keyUp)
    {
        var virtualKey = InputKeyCodeMapper.ToWindowsVirtualKey(keyCode);
        if (virtualKey == 0) return;

        var input = new Input
        {
            Type = InputKeyboard,
            U = new InputUnion
            {
                Ki = new KeyboardInput
                {
                    Wvk = virtualKey,
                    DwFlags = keyUp ? KeyEventFKeyUp : 0
                }
            }
        };

        SendInputOrThrow(input);
    }

    private static void SendUnicodeChar(char c, bool keyUp)
    {
        var input = new Input
        {
            Type = InputKeyboard,
            U = new InputUnion
            {
                Ki = new KeyboardInput
                {
                    WScan = c,
                    DwFlags = KeyEventFUnicode | (keyUp ? KeyEventFKeyUp : 0)
                }
            }
        };

        SendInputOrThrow(input);
    }

    private static void SendMouseInput(uint flags, int dx = 0, int dy = 0, int mouseData = 0)
    {
        var input = new Input
        {
            Type = InputMouse,
            U = new InputUnion
            {
                Mi = new MouseInput
                {
                    Dx = dx,
                    Dy = dy,
                    MouseData = mouseData,
                    DwFlags = flags
                }
            }
        };

        SendInputOrThrow(input);
    }

    private static (uint Flags, int MouseData) GetMouseButtonFlags(MouseButton button, bool down)
    {
        return button switch
        {
            MouseButton.Button1 => (down ? MouseEventFLeftDown : MouseEventFLeftUp, 0),
            MouseButton.Button2 => (down ? MouseEventFRightDown : MouseEventFRightUp, 0),
            MouseButton.Button3 => (down ? MouseEventFMiddleDown : MouseEventFMiddleUp, 0),
            MouseButton.Button4 => (down ? MouseEventFXDown : MouseEventFXUp, 1),
            MouseButton.Button5 => (down ? MouseEventFXDown : MouseEventFXUp, 2),
            _ => (0, 0)
        };
    }

    private static (int X, int Y) NormalizeAbsolutePoint(int x, int y)
    {
        var left = GetSystemMetrics(SmXVirtualScreen);
        var top = GetSystemMetrics(SmYVirtualScreen);
        var width = Math.Max(1, GetSystemMetrics(SmCxVirtualScreen));
        var height = Math.Max(1, GetSystemMetrics(SmCyVirtualScreen));
        var normalizedX = (int)Math.Round((x - left) * 65535.0 / (width - 1));
        var normalizedY = (int)Math.Round((y - top) * 65535.0 / (height - 1));
        return (Math.Clamp(normalizedX, 0, 65535), Math.Clamp(normalizedY, 0, 65535));
    }

    private static void SendInputOrThrow(Input input)
    {
        Input[] inputs = { input };
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        if (sent == 0)
        {
            throw new InvalidOperationException(
                $"SendInput failed. Win32Error={Marshal.GetLastWin32Error()}. If the target game runs as administrator, run UiharuMind with the same privilege.");
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Input
    {
        public int Type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MouseInput Mi;
        [FieldOffset(0)] public KeyboardInput Ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput
    {
        public int Dx;
        public int Dy;
        public int MouseData;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Wvk;
        public ushort WScan;
        public uint DwFlags;
        public uint Time;
        public UIntPtr DwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, Input[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);
}
