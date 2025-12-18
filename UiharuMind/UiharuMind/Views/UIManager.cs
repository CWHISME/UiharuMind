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
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using UiharuMind.Core.AI.Memery;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.RemoteOpenAI;
using UiharuMind.Utils;
using UiharuMind.ViewModels.ViewData;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.Characters;
using UiharuMind.Views.Windows.Common;
using UiharuMind.Views.Windows.ScreenCapture;

namespace UiharuMind.Views;

public static class UIManager
{
    // private static Dictionary<Type, UiharuWindowBase> _windows = new Dictionary<Type, UiharuWindowBase>();

    // public static bool IsClosing => ClosingWindowSet.Count > 0; //{ get; set; } = false;
    // public static HashSet<UiharuWindowBase> ClosingWindowSet { get; set; } = new HashSet<UiharuWindowBase>();

    private static Dictionary<Type, List<UiharuWindowBase>> _multiWindows =
        new Dictionary<Type, List<UiharuWindowBase>>();

    private static Stack<Window> _windowStack = new Stack<Window>();

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
                if (win.IsVisible && isMulti) continue;
                window = (T)win;
                break;
            }

            if (window != null)
            {
                action?.Invoke((T)window);
                window.RequestShow(isActivate: isActivate);
            }
            else
            {
                window = new T();
                // _multiWindows[typeof(T)] = [window];
                if (window.IsCacheWindow) windowsList.Add(window);
                onCreateCallback?.Invoke(window);
                action?.Invoke(window);
                window.WindowStartupLocation = WindowStartupLocation.Manual;
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
    public static Window GetFoucusWindow()
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
        if (_multiWindows.ContainsKey(typeof(T)))
        {
            var window = _multiWindows[typeof(T)][0];
            window.Close();
        }
    }

    /// <summary>
    ///  在屏幕显示一张截图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    /// <param name="startMousePos">开始截图的鼠标位置</param>
    /// <param name="endMousePos">结束截图的鼠标位置</param>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, PixelPoint startMousePos, PixelPoint endMousePos)
    {
        ShowPreviewImageWindowAtMousePosition(image, null,
            endMousePos.X > startMousePos.X ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            endMousePos.Y > startMousePos.Y ? VerticalAlignment.Top : VerticalAlignment.Bottom);
    }

    /// <summary>
    /// 在屏幕显示一张图(当前鼠标位置)
    /// </summary>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, Size? size = null, HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left,
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

        ShowWindow<ScreenCapturePreviewWindow>((window) => { window.SetImage(image, size, null, horizontalAlignment, verticalAlignment); }, isMulti: true);
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
        return await window.ShowDialog<string?>(owner ?? UIManager.GetFoucusWindow());
    }

    public static void ShowEditCharacterWindow(CharacterInfoViewData? characterInfo,
        Action<CharacterInfoViewData>? onSureCallback)
    {
        characterInfo ??= new CharacterInfoViewData() { IsRole = false };
        UIManager.ShowWindow<CharacterEditWindow>(x =>
        {
            x.DataContext = characterInfo;
            x.OnSureCallback = onSureCallback;
        });
    }

    public static void ShowMemorySelectWindow(Window owner, Action<MemoryData>? onSelectMemory,
        MemoryData? selectedMemory)
    {
        var window = new MemorySelectWindow()
        {
            DataContext = new MemorySelectWindowModel(selectedMemory, onSelectMemory)
        };
        window.ShowDialogStackWindow(owner);
    }

    public static void ShowMemoryEditorWindow(Window owner, MemoryData memoryData, Action? onClose = null)
    {
        var window = new MemoryEditorWindow
        {
            DataContext = new MemoryEditorWindowModel(memoryData, onClose)
        };
        window.ShowDialogStackWindow(owner);
    }
}