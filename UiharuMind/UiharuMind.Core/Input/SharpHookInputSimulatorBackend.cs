using SharpHook;
using SharpHook.Data;

namespace UiharuMind.Core.Input;

public class SharpHookInputSimulatorBackend : IInputSimulatorBackend
{
    private readonly EventSimulator _globalSimulator = new();

    public void SendMouseMove(short x, short y)
    {
        _globalSimulator.SimulateMouseMovement(x, y);
    }

    public async Task SendMouseClick(MouseButton button, int delayMs = 100)
    {
        _globalSimulator.SimulateMousePress(button);
        await Task.Delay(delayMs);
        _globalSimulator.SimulateMouseRelease(button);
    }

    public void SimulateMousePress(MouseButton button)
    {
        _globalSimulator.SimulateMousePress(button);
    }

    public void SimulateMouseRelease(MouseButton button)
    {
        _globalSimulator.SimulateMouseRelease(button);
    }

    public void SimulateMouseWheel(int wheelDelta)
    {
        _globalSimulator.SimulateMouseWheel(
            ClampToShort(wheelDelta),
            MouseWheelScrollDirection.Vertical,
            MouseWheelScrollType.UnitScroll);
    }

    private static short ClampToShort(int value)
    {
        return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
    }

    public async Task SendKeyPress(KeyCode keyCode, int delayMs = 100)
    {
        _globalSimulator.SimulateKeyPress(keyCode);
        await Task.Delay(delayMs);
        _globalSimulator.SimulateKeyRelease(keyCode);
    }

    public void SimulateKeyPress(KeyCode keyCode)
    {
        _globalSimulator.SimulateKeyPress(keyCode);
    }

    public void SimulateKeyRelease(KeyCode keyCode)
    {
        _globalSimulator.SimulateKeyRelease(keyCode);
    }

    public async Task SendText(string text, int delayBetweenKeys = 50)
    {
        foreach (char c in text)
        {
            var keyCode = InputKeyCodeMapper.CharToKeyCode(c);
            if (keyCode == KeyCode.VcUndefined) continue;
            await SendKeyPress(keyCode, delayBetweenKeys);
        }
    }
}
