using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Threading;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.ScreenCapture;

namespace UiharuMind.Views;

public class UIManager
{
    private static Dictionary<Type, UiharuWindowBase> _windows = new Dictionary<Type, UiharuWindowBase>();

    /// <summary>
    /// 开启一个界面
    /// </summary>
    /// <param name="action">每次开启后都会调用</param>
    /// <param name="onCreateCallback">仅当处于第一次创建时才会调用，后续打开时只会调用 action</param>
    /// <typeparam name="T"></typeparam>
    public static void ShowWindow<T>(Action<T>? action = null, Action<T>? onCreateCallback = null)
        where T : UiharuWindowBase, new()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            if (_windows.ContainsKey(typeof(T)))
            {
                var window = _windows[typeof(T)];
                action?.Invoke((T)window);
                window.RequestShow();
            }
            else
            {
                var window = new T();
                _windows.Add(typeof(T), window);
                onCreateCallback?.Invoke(window);
                action?.Invoke(window);
                window.Awake();
                window.RequestShow();
            }
        });
    }

    public static T? GetWindow<T>()
        where T : UiharuWindowBase
    {
        if (_windows.ContainsKey(typeof(T)))
        {
            return (T)_windows[typeof(T)];
        }

        return null;
    }
}