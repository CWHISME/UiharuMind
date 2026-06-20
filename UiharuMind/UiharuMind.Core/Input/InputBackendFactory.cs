using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Input;

internal static class InputBackendFactory
{
    public static IInputHookBackend CreateHookBackend()
    {
        return PlatformUtils.IsWindows ? new WindowsInputHookBackend() : new SharpHookInputHookBackend();
    }

    public static IInputSimulatorBackend CreateSimulatorBackend()
    {
        return PlatformUtils.IsWindows ? new WindowsInputSimulatorBackend() : new SharpHookInputSimulatorBackend();
    }
}
