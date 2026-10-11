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

    /// <summary>
    /// 不按键位直接打出一段字（中文、符号都行，不受输入法与键盘布局影响）；做不到的后端尽量按键位打
    /// </summary>
    /// <param name="text">要打的字</param>
    void SendUnicodeText(string text);
}
