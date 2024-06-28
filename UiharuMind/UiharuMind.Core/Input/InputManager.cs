using SharpHook;
using SharpHook.Native;
using UiharuMind.Core.Core;

namespace UiharuMind.Core.Input;

public class InputManager
{
    public static MouseEventData MouseData;

    public async void Start()
    {
        var hook = new TaskPoolGlobalHook();

        hook.HookEnabled += OnHookEnabled; // EventHandler<HookEventArgs>
        hook.HookDisabled += OnHookDisabled; // EventHandler<HookEventArgs>

        // hook.KeyTyped += OnKeyTyped; // EventHandler<KeyboardHookEventArgs>
        // hook.KeyPressed += OnKeyPressed; // EventHandler<KeyboardHookEventArgs>
        // hook.KeyReleased += OnKeyReleased; // EventHandler<KeyboardHookEventArgs>

        hook.MouseClicked += OnMouseClicked; // EventHandler<MouseHookEventArgs>
        hook.MousePressed += OnMousePressed; // EventHandler<MouseHookEventArgs>
        hook.MouseReleased += OnMouseReleased; // EventHandler<MouseHookEventArgs>
        hook.MouseMoved += OnMouseMoved; // EventHandler<MouseHookEventArgs>
        hook.MouseDragged += OnMouseDragged; // EventHandler<MouseHookEventArgs>

        hook.MouseWheel += OnMouseWheel; // EventHandler<MouseWheelHookEventArgs>

        await hook.RunAsync();
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