using SharpHook;
using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Input;

public class InputSimulateManager : Singleton<InputSimulateManager>, IInitialize
{
    EventSimulator _globalSimulator = new EventSimulator();

    public void OnInitialize()
    {
    }

    public void SendMouseMove(short x, short y)
    {
        _globalSimulator.SimulateMouseMovement(x, y);
    }

    public async void SendMouseClickLeft()
    {
        try
        {
            _globalSimulator.SimulateMousePress(MouseButton.Button1);
            await Task.Delay(100);
            _globalSimulator.SimulateMouseRelease(MouseButton.Button1);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public async Task SendMouseClick(MouseButton button, int delayMs = 100)
    {
        try
        {
            _globalSimulator.SimulateMousePress(button);
            await Task.Delay(delayMs);
            _globalSimulator.SimulateMouseRelease(button);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void SimulateMousePress(MouseButton button)
    {
        try
        {
            _globalSimulator.SimulateMousePress(button);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void SimulateMouseRelease(MouseButton button)
    {
        try
        {
            _globalSimulator.SimulateMouseRelease(button);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void SimulateMouseWheel(int wheelDelta)
    {
        try
        {
            _globalSimulator.SimulateMouseWheel((short)wheelDelta);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public async Task SendKeyPress(KeyCode keyCode, int delayMs = 100)
    {
        try
        {
            _globalSimulator.SimulateKeyPress(keyCode);
            await Task.Delay(delayMs);
            _globalSimulator.SimulateKeyRelease(keyCode);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void SimulateKeyPress(KeyCode keyCode)
    {
        try
        {
            _globalSimulator.SimulateKeyPress(keyCode);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void SimulateKeyRelease(KeyCode keyCode)
    {
        try
        {
            _globalSimulator.SimulateKeyRelease(keyCode);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public async Task SendText(string text, int delayBetweenKeys = 50)
    {
        try
        {
            foreach (char c in text)
            {
                var keyCode = CharToKeyCode(c);
                if (keyCode != KeyCode.VcUndefined)
                {
                    await SendKeyPress(keyCode, delayBetweenKeys);
                }
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    private KeyCode CharToKeyCode(char c)
    {
        return c switch
        {
            >= 'a' and <= 'z' => (KeyCode)((int)KeyCode.VcA + (c - 'a')),
            >= 'A' and <= 'Z' => (KeyCode)((int)KeyCode.VcA + (c - 'A')),
            >= '0' and <= '9' => (KeyCode)((int)KeyCode.Vc0 + (c - '0')),
            ' ' => KeyCode.VcSpace,
            '.' => KeyCode.VcPeriod,
            ',' => KeyCode.VcComma,
            ';' => KeyCode.VcSemicolon,
            '\''=> KeyCode.VcQuote,
            '[' => KeyCode.VcOpenBracket,
            ']' => KeyCode.VcCloseBracket,
            '\\' => KeyCode.VcBackslash,
            '/' => KeyCode.VcSlash,
            '-' => KeyCode.VcMinus,
            '=' => KeyCode.VcEquals,
            _ => KeyCode.VcUndefined
        };
    }

    public void Test()
    {
        _globalSimulator.Sequence()
            .AddMouseMovementRelative(20, 20)
            .AddMousePress(MouseButton.Button1)
            .AddMouseRelease(MouseButton.Button1)
            .AddMouseMovementRelative(-20, -20)
            .Simulate();
    }
}