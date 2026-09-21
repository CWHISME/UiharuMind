using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Input;

public class InputSimulateManager : Singleton<InputSimulateManager>, IInitialize
{
    private readonly IInputSimulatorBackend _simulatorBackend = InputBackendFactory.CreateSimulatorBackend();

    public void OnInitialize()
    {
    }

    public void SendMouseMove(short x, short y)
    {
        _simulatorBackend.SendMouseMove(x, y);
    }

    public void SendMouseMoveRelative(short x, short y)
    {
        _simulatorBackend.SendMouseMoveRelative(x, y);
    }

    public async Task SendMouseClickLeft()
    {
        await SendMouseClick(MouseButton.Button1);
    }

    public async Task SendMouseClick(MouseButton button, int delayMs = 100)
    {
        try
        {
            await _simulatorBackend.SendMouseClick(button, delayMs);
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
            _simulatorBackend.SimulateMousePress(button);
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
            _simulatorBackend.SimulateMouseRelease(button);
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
            _simulatorBackend.SimulateMouseWheel(wheelDelta);
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
            await _simulatorBackend.SendKeyPress(keyCode, delayMs);
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
            _simulatorBackend.SimulateKeyPress(keyCode);
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
            _simulatorBackend.SimulateKeyRelease(keyCode);
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
            await _simulatorBackend.SendText(text, delayBetweenKeys);
        }
        catch (Exception e)
        {
            Log.Error(e);
        }
    }

    public void Test()
    {
        SendMouseMove((short)(InputManager.MouseData.X + 20), (short)(InputManager.MouseData.Y + 20));
        SimulateMousePress(MouseButton.Button1);
        SimulateMouseRelease(MouseButton.Button1);
        SendMouseMove((short)(InputManager.MouseData.X - 20), (short)(InputManager.MouseData.Y - 20));
    }
}
