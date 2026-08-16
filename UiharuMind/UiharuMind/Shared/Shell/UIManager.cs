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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Shared.Services;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture;

namespace UiharuMind.Shared.Shell;

public static class UIManager
{
    // private static Dictionary<Type, UiharuWindowBase> _windows = new Dictionary<Type, UiharuWindowBase>();

    // public static bool IsClosing => ClosingWindowSet.Count > 0; //{ get; set; } = false;
    // public static HashSet<UiharuWindowBase> ClosingWindowSet { get; set; } = new HashSet<UiharuWindowBase>();

    private static Dictionary<Type, List<UiharuWindowBase>> _multiWindows = new();
    private static Stack<Window> _windowStack = new Stack<Window>();
    private static HashSet<Type> _creatingWindows = new();

    /// <summary>
    /// 开启一个界面
    /// </summary>
    /// <param name="action">每次开启后都会调用</param>
    /// <param name="onCreateCallback">仅当处于第一次创建时才会调用，后续打开时只会调用 action</param>
    /// <param name="isMulti">允许同时开启多个同一窗口</param>
    /// <param name="isActivate">是否同时激活(聚焦)窗口</param>
    /// <typeparam name="T"></typeparam>
    public static void ShowWindow<T>(Action<T>? action = null, Action<T>? onCreateCallback = null, bool isMulti = false,
        bool isActivate = true)
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
                if (!win.IsCacheWindow) continue;
                if (win.IsVisible && isMulti) continue;
                window = (T)win;
                break;
            }

            if (window != null)
            {
                action?.Invoke((T)window);
                window.RequestShow(isActivate: isActivate);
            }
            else if (windowsList.Count == 0 || isMulti)
            {
                if (!_creatingWindows.Add(typeof(T)))
                {
                    Log.Warning($"[{typeof(T).Name}] Creation already in progress, skipping duplicate.");
                    return;
                }

                try
                {
                    window = new T();
                    windowsList.Add(window);
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    onCreateCallback?.Invoke(window);
                    action?.Invoke(window);
                    window.Awake();
                    window.RequestShow(true);
                }
                finally
                {
                    _creatingWindows.Remove(typeof(T));
                }
            }
            else
            {
                if (windowsList[0].ContributesToMacRegularMode) WindowActivationService.Activate(windowsList[0]);
                Log.Warning($"[{typeof(T).Name}] This window is already opened.");
            }

            RefreshMacApplicationActivationPolicy();
            if (isActivate && window?.ContributesToMacRegularMode == true)
                MacApplicationActivationService.ActivateIgnoringOtherApps();
        }, DispatcherPriority.Render);
    }

    public static T? GetWindow<T>()
        where T : UiharuWindowBase
    {
        if (_multiWindows.TryGetValue(typeof(T), out var windows) && windows.Count > 0)
        {
            return (T)windows[0];
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

    // public static MainWindow? GetMainWindow()
    // {
    //     var mainWindow = GetWindow<MainWindow>();
    //     if (mainWindow?.IsVisible == true) return mainWindow;
    //     return null;
    // }

    /// <summary>
    /// 当前焦点窗口
    /// </summary>
    /// <returns></returns>
    public static Window GetFocusWindow()
    {
        if (_windowStack.Count > 0) return _windowStack.Peek();
        Window? selectedWindow = null;
        foreach (var window in _multiWindows)
        {
            foreach (var win in window.Value)
            {
                if (win.IsFocused) return win;
                if (win.IsActive && win.IsVisible && win.WindowState != WindowState.Minimized) selectedWindow = win;
            }
        }

        return selectedWindow ?? GetRootWindow();
    }

    public static void CloseWindow<T>()
        where T : UiharuWindowBase
    {
        CloseWindow(typeof(T));
    }

    public static void CloseWindow(Type type)
    {
        if (_multiWindows.TryGetValue(type, out var windows) && windows.Count > 0)
        {
            windows[0].Close();
        }
    }

    public static void RemoveWindow(UiharuWindowBase win)
    {
        if (_multiWindows.TryGetValue(win.GetType(), out var windows) && windows.Count > 0)
        {
            windows.Remove(win);
        }
    }

    public static void RefreshMacApplicationActivationPolicy()
    {
        MacApplicationActivationService.SetRegularMode(HasVisibleMacRegularModeWindow());
    }

    private static bool HasVisibleMacRegularModeWindow()
    {
        foreach (var window in _multiWindows.Values)
        {
            foreach (var win in window)
            {
                if (win.ContributesToMacRegularMode && win.IsVisible && win.WindowState != WindowState.Minimized)
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    ///  在屏幕显示一张截图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    /// <param name="startMousePos">开始截图的鼠标位置</param>
    /// <param name="endMousePos">结束截图的鼠标位置</param>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, PixelPoint startMousePos,
        PixelPoint endMousePos)
    {
        ShowPreviewImageWindowAtMousePosition(image, null,
            endMousePos.X > startMousePos.X ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            endMousePos.Y > startMousePos.Y ? VerticalAlignment.Top : VerticalAlignment.Bottom);
    }

    /// <summary>
    /// 预览一张调用方仍然持有的图（气泡里的图、剪贴板缓存的图等）。
    /// 预览窗把图当成自己的、关窗即释放，所以这里只交给它一份副本——
    /// 否则关掉预览之后调用方那张图已被释放，再点一次就炸。
    /// </summary>
    /// <param name="image">调用方持有的图，本方法不会改动或释放它</param>
    /// <param name="size">显示尺寸，默认取图片原始尺寸</param>
    /// <param name="horizontalAlignment">相对鼠标的水平对齐</param>
    /// <param name="verticalAlignment">相对鼠标的垂直对齐</param>
    public static void ShowPreviewImageCopyWindowAtMousePosition(Bitmap? image, Size? size = null,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment verticalAlignment = VerticalAlignment.Center)
    {
        if (image == null)
        {
            Log.Warning("image is null");
            return;
        }

        ShowPreviewImageWindowAtMousePosition(image.CloneBitmap(), size, horizontalAlignment, verticalAlignment);
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)。图片的生命周期由预览窗接管，关闭时会被释放，
    /// 调用方若还要继续使用这张图，请改用 <see cref="ShowPreviewImageCopyWindowAtMousePosition"/>。
    /// </summary>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, Size? size = null,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment verticalAlignment = VerticalAlignment.Top)
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

        ShowWindow<ScreenCapturePreviewWindow>(
            (window) => { window.SetImage(image, size, null, horizontalAlignment, verticalAlignment); }, isMulti: true);
    }

    public static async void ShowDialogStackWindow(this Window target, Window owner)
    {
        try
        {
            _windowStack.Push(target);
            await target.ShowDialog(owner);
            _windowStack.Pop();
        }
        catch (Exception e)
        {
            Log.Error(e.Message);
        }
        finally
        {
            if (_windowStack.Count > 0 && _windowStack.Peek() == target) _windowStack.Pop();
        }
    }

//===================open====================
    public static async Task<string?> ShowStringEditWindow(string content, Window? owner = null)
    {
        StringContentEditWindow window = new StringContentEditWindow();
        if (IconUtils.DefaultAppIcon != null) window.Icon = new WindowIcon(IconUtils.DefaultAppIcon);
        window.DataContext = new StringContentEditWindowViewModel(content, null);
        return await window.ShowDialog<string?>(owner ?? UIManager.GetFocusWindow());
    }

    // feature 专属的窗口打开器住在各自 feature 里:
    // 角色见 Features/Characters/CharacterWindows,知识库见 Features/Memory/MemoryWindows。
    // 这里只留通用机制(窗口栈、焦点窗口、字符串编辑、图片预览)
}