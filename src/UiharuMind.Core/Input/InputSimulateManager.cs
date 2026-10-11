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

    /// <summary>
    /// 往前台应用发一次「粘贴」：macOS 是 Cmd+V，别处 Ctrl+V
    /// </summary>
    /// <returns>按完松开后完成</returns>
    public async Task SendPaste()
    {
        KeyCode modifier = OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;
        SimulateKeyPress(modifier);
        await Task.Delay(20);
        await SendKeyPress(KeyCode.VcV, 20);
        SimulateKeyRelease(modifier);
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

    /// <summary>
    /// 不按键位直接打出一段字（中文也行）
    /// </summary>
    /// <param name="text">要打的字</param>
    public void SendUnicodeText(string text)
    {
        try
        {
            _simulatorBackend.SendUnicodeText(text);
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
