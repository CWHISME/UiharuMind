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

    public static void ShowWindow<T>() where T : UiharuWindowBase, new()
    {
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (_windows.ContainsKey(typeof(T)))
            {
                _windows[typeof(T)].RequestShow();
            }
            else
            {
                T window = new T();
                _windows.Add(typeof(T), window);
                window.RequestShow();
            }
        });
    }
}