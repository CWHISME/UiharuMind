using SharpHook;
using SharpHook.Native;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Input;

public class InputManager
{
    public static MouseEventData MouseData;

    private TaskPoolGlobalHook _hook;

    public async void Start()
    {
        _hook = new TaskPoolGlobalHook();

        _hook.HookEnabled += OnHookEnabled; // EventHandler<HookEventArgs>
        _hook.HookDisabled += OnHookDisabled; // EventHandler<HookEventArgs>

        // hook.KeyTyped += OnKeyTyped; // EventHandler<KeyboardHookEventArgs>
        // hook.KeyPressed += OnKeyPressed; // EventHandler<KeyboardHookEventArgs>
        // hook.KeyReleased += OnKeyReleased; // EventHandler<KeyboardHookEventArgs>

        _hook.MouseClicked += OnMouseClicked; // EventHandler<MouseHookEventArgs>
        _hook.MousePressed += OnMousePressed; // EventHandler<MouseHookEventArgs>
        _hook.MouseReleased += OnMouseReleased; // EventHandler<MouseHookEventArgs>
        _hook.MouseMoved += OnMouseMoved; // EventHandler<MouseHookEventArgs>
        _hook.MouseDragged += OnMouseDragged; // EventHandler<MouseHookEventArgs>

        _hook.MouseWheel += OnMouseWheel; // EventHandler<MouseWheelHookEventArgs>

        await _hook.RunAsync();
    }

    ~InputManager()
    {
        _hook.Dispose();
    }

    private void OnMouseClicked(object? sender, MouseHookEventArgs e)
    {
        Log.Debug(sender + "  OnMouseClicked " + e);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        Log.Debug("OnMousePressed");
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        Log.Debug("OnMouseReleased");
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        MouseData = e.Data;
        // Log.Debug("OnMouseMoved");
    }

    private void OnMouseDragged(object? sender, MouseHookEventArgs e)
    {
        Log.Debug("OnMouseDragged");
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        // Log.Debug("OnMouseWheel");
    }

    private void OnHookEnabled(object? sender, HookEventArgs e)
    {
        Log.Debug("OnHookEnabled");
    }

    private void OnHookDisabled(object? sender, HookEventArgs e)
    {
        Log.Debug("OnHookDisabled");
    }
}