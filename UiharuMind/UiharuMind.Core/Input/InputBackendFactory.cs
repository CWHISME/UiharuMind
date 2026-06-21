namespace UiharuMind.Core.Input;

internal static class InputBackendFactory
{
    public static IInputHookBackend CreateHookBackend()
    {
        return new SharpHookInputHookBackend();
    }

    public static IInputSimulatorBackend CreateSimulatorBackend()
    {
        return new SharpHookInputSimulatorBackend();
    }
}
