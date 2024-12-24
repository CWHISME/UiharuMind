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

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.ScreenCapture;

namespace UiharuMind.Views;

public class UIManager
{
    // private static Dictionary<Type, UiharuWindowBase> _windows = new Dictionary<Type, UiharuWindowBase>();

    private static Dictionary<Type, List<UiharuWindowBase>> _multiWindows =
        new Dictionary<Type, List<UiharuWindowBase>>();

    /// <summary>
    /// 开启一个界面
    /// </summary>
    /// <param name="action">每次开启后都会调用</param>
    /// <param name="onCreateCallback">仅当处于第一次创建时才会调用，后续打开时只会调用 action</param>
    /// <param name="isMulti">允许同时开启多个同一窗口</param>
    /// <typeparam name="T"></typeparam>
    public static void ShowWindow<T>(Action<T>? action = null, Action<T>? onCreateCallback = null, bool isMulti = false)
        where T : UiharuWindowBase, new()
    {
        Dispatcher.UIThread.Invoke(() =>
        {
            _multiWindows.TryGetValue(typeof(T), out var windowsList);
            if (windowsList == null)
            {
                windowsList = new List<UiharuWindowBase>();
                _multiWindows[typeof(T)] = windowsList;
            }

            T? window = null;
            foreach (var win in windowsList)
            {
                if (win.IsVisible && isMulti) continue;
                window = (T)win;
                break;
            }

            if (window != null)
            {
                action?.Invoke((T)window);
                window.RequestShow();
            }
            else
            {
                window = new T();
                // _multiWindows[typeof(T)] = [window];
                windowsList.Add(window);
                onCreateCallback?.Invoke(window);
                action?.Invoke(window);
                window.Awake();
                window.RequestShow(true);
            }
            // if (_multiWindows.ContainsKey(typeof(T)))
            // {
            //     var window = _multiWindows[typeof(T)][0];
            //     action?.Invoke((T)window);
            //     window.RequestShow();
            // }
            // else
            // {
            //     var window = new T();
            //     _multiWindows[typeof(T)] = [window];
            //     onCreateCallback?.Invoke(window);
            //     action?.Invoke(window);
            //     window.Awake();
            //     window.RequestShow();
            // }
        }, DispatcherPriority.Render);
    }

    public static T? GetWindow<T>()
        where T : UiharuWindowBase
    {
        if (_multiWindows.ContainsKey(typeof(T)))
        {
            return (T)_multiWindows[typeof(T)][0];
        }

        return null;
    }

    /// <summary>
    /// 获取一个主口，优先查找 MainWindow，如果没有打开或处于隐藏状态则返回 DummyWindow
    /// </summary>
    /// <returns></returns>
    public static Window GetRootWindow()
    {
        var mainWindow = GetWindow<MainWindow>();
        if (mainWindow?.IsVisible == true) return mainWindow;
        return App.DummyWindow;
    }

    public static MainWindow? GetMainWindow()
    {
        var mainWindow = GetWindow<MainWindow>();
        if (mainWindow?.IsVisible == true) return mainWindow;
        return null;
    }

    /// <summary>
    /// 当前焦点窗口
    /// </summary>
    /// <returns></returns>
    public static Window GetFoucusWindow()
    {
        foreach (var window in _multiWindows)
        {
            foreach (var win in window.Value)
            {
                if (win.IsActive) return win;
            }
        }

        return GetRootWindow();
    }

    public static void CloseWindow<T>()
        where T : UiharuWindowBase
    {
        if (_multiWindows.ContainsKey(typeof(T)))
        {
            var window = _multiWindows[typeof(T)][0];
            window.Close();
        }
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    /// <param name="size">默认大小</param>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, Size? size = null)
    {
        if (image == null)
        {
            Log.Warning("image is null");
            return;
        }

        if (image.PixelSize.Width < 5 || image.PixelSize.Height < 5)
        {
            Log.Warning("image PixelSize is too small");
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var window = new ScreenCapturePreviewWindow();
            window.SetImage(image, size);
            window.Show();
        });
    }
}