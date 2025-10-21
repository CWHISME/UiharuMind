/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using SharpHook;
using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.Input;

public class InputManager : Singleton<InputManager>, IInitialize
{
    /// <summary>
    /// 当前鼠标信息
    /// </summary>
    public static MouseEventData MouseData;

    /// <summary>
    /// 上一次鼠标按下位置信息
    /// </summary>
    public static MouseEventData MousePressedData;

    /// <summary>
    /// 上一次鼠标释放位置信息
    /// </summary>
    public static MouseEventData MouseReleasedData;

    /// <summary>
    /// 功能是否启用
    /// </summary>
    public bool IsRunning => _hook.IsRunning;

    public event Action<KeyCode>? EventOnKeyDown;
    public event Action<KeyCode>? EventOnKeyUp;
    public event Action<MouseEventData>? EventOnMouseClicked;
    public event Action<MouseWheelEventData>? EventOnMouseWheel;

    private GlobalHookBase _hook;

    private HashSet<KeyCode> _pressedKeys = new HashSet<KeyCode>();

    /// <summary>
    /// 组合键组合数据
    /// </summary>
    private List<KeyCombinationData> _keyCombinations = new List<KeyCombinationData>();

    //是否启用过
    private bool _isEnabled;
    // private Action _onStartCallback;

    public void OnInitialize()
    {
        //Test
        // if(UiharuCoreManager.Instance.IsWindows) return;

        // Start();
    }

    public async void Start(Action onFailed)
    {
        try
        {
            // _onStartCallback = onStart;

            _hook = new SimpleGlobalHook(GlobalHookType.All); //new TaskPoolGlobalHook();

            _hook.HookEnabled += OnHookEnabled;
            _hook.HookDisabled += OnHookDisabled;

            // _hook.KeyTyped += OnKeyTyped;
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;

            // _hook.MouseClicked += OnMouseClicked;
            _hook.MousePressed += OnMousePressed;
            _hook.MouseReleased += OnMouseReleased;
            _hook.MouseMoved += OnMouseMoved;
            _hook.MouseDragged += OnMouseDragged;

            _hook.MouseWheel += OnMouseWheel;

            //     Thread thread = new Thread(() =>
            //     {
            //         try
            //         {
            //             _hook.Run();
            //             onStarted?.Invoke();
            //         }
            //         catch (Exception e)
            //         {
            //             Console.WriteLine(e);
            //             onFailed?.Invoke();
            //         }
            //     });
            //     thread.Start();
            //     thread.Interrupt();
            // }
            // catch (Exception)
            // {
            //     Stop();
            // }

            await _hook.RunAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
            Stop();
            if (!_isEnabled) onFailed.Invoke();
        }
    }

    public void Stop()
    {
        if (_hook.IsDisposed) return;
        try
        {
            _hook.Dispose();
        }
        catch (Exception)
        {
            // ignored
        }
    }

    public bool IsPressed(KeyCode keyCode)
    {
        return _pressedKeys.Contains(keyCode);
    }

    public void RegisterKey(KeyCombinationData keyCombination)
    {
        _keyCombinations.Add(keyCombination);
    }

    public void UnRegisterKey(KeyCombinationData keyCombination)
    {
        _keyCombinations.Remove(keyCombination);
    }

    //=========Event Handler=========

    private void OnKeyTyped(object? sender, KeyboardHookEventArgs e)
    {
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        EventOnKeyDown?.Invoke(e.Data.KeyCode);
        foreach (var keyCombination in _keyCombinations)
        {
            if (keyCombination.MainKeyCode != e.Data.KeyCode) continue;
            if (keyCombination.DecorateKeyCodes != null && !keyCombination.DecorateKeyCodes.All(IsPressed)) continue;
            keyCombination.OnTrigger?.Invoke();
            e.SuppressEvent = true;
            return;
        }

        _pressedKeys.Add(e.Data.KeyCode);
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        EventOnKeyUp?.Invoke(e.Data.KeyCode);
        _pressedKeys.Remove(e.Data.KeyCode);
    }

    private void OnMouseClicked(object? sender, MouseHookEventArgs e)
    {
        // Log.Debug(sender + "  OnMouseClicked " + e);
    }

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        EventOnMouseClicked?.Invoke(e.Data);
        MouseData = e.Data;
        MousePressedData = e.Data;
        // Log.Debug("OnMousePressed");
    }

    private void OnMouseReleased(object? sender, MouseHookEventArgs e)
    {
        MouseData = e.Data;
        MouseReleasedData = e.Data;
        // Log.Debug("OnMouseReleased");
    }

    private void OnMouseMoved(object? sender, MouseHookEventArgs e)
    {
        MouseData = e.Data;
        // Log.Debug($"鼠标位置 {e.Data.X},{e.Data.Y}");
    }

    private void OnMouseDragged(object? sender, MouseHookEventArgs e)
    {
        MouseData = e.Data;
        // Log.Debug("OnMouseDragged");
    }

    private void OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
    {
        EventOnMouseWheel?.Invoke(e.Data);
    }

    private void OnHookEnabled(object? sender, HookEventArgs e)
    {
        Log.Debug("OnHookEnabled");
        _isEnabled = true;
        // _onStartCallback.Invoke();
    }

    private void OnHookDisabled(object? sender, HookEventArgs e)
    {
        Log.Debug("OnHookDisabled");
    }
}