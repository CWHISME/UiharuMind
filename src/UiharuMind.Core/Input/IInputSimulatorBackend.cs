using SharpHook.Data;

namespace UiharuMind.Core.Input;

public interface IInputSimulatorBackend
{
    void SendMouseMove(short x, short y);
    void SendMouseMoveRelative(short x, short y);
    Task SendMouseClick(MouseButton button, int delayMs = 100);
    void SimulateMousePress(MouseButton button);
    void SimulateMouseRelease(MouseButton button);
    void SimulateMouseWheel(int wheelDelta);
    Task SendKeyPress(KeyCode keyCode, int delayMs = 100);
    void SimulateKeyPress(KeyCode keyCode);
    void SimulateKeyRelease(KeyCode keyCode);
    Task SendText(string text, int delayBetweenKeys = 50);
}
