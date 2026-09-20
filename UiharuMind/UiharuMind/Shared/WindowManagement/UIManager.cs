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
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Utils;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Features.ScreenCapture;

namespace UiharuMind.Shared.WindowManagement;

/// <summary>
/// UI 门面：窗口打开/关闭/聚焦等编排入口。窗口存哪、缓存怎么留，
/// 分别在 <see cref="WindowRegistry"/> 与 <see cref="WindowCache"/>。
/// </summary>
public static class UIManager
{
    private static readonly IApplicationActivationPolicy _activationPolicy =
        ApplicationActivationPolicyFactory.Create();

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
            var windowsList = WindowRegistry.GetOrCreateList(typeof(T));

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
                if (windowsList[0].ContributesToMacRegularMode) windowsList[0].RequestFocus();
                Log.Warning($"[{typeof(T).Name}] This window is already opened.");
            }

            RefreshMacApplicationActivationPolicy();
            // 辅助窗口（快捷面板、浮窗）排除在外：整应用激活会把后台的主界面一起抬到前台
            if (isActivate && window is { ContributesToMacRegularMode: true, IsAuxiliaryWindow: false })
                _activationPolicy.ActivateIgnoringOtherApps();
        }, DispatcherPriority.Render);
    }

    /// <summary>
    /// 取某一类<b>已创建</b>的窗口，含已关闭待复用的缓存窗口。按什么条件挑由调用方决定
    /// （<see cref="ShowWindow{T}"/> 只认类型，认不出"同一个会话的那扇窗"这种业务身份）。
    /// </summary>
    /// <typeparam name="T">窗口类型</typeparam>
    /// <returns>该类型的窗口列表；一个都没创建过时为空</returns>
    public static IReadOnlyList<UiharuWindowBase> GetWindows<T>()
        where T : UiharuWindowBase
    {
        return WindowRegistry.GetWindows(typeof(T));
    }

    public static T? GetWindow<T>()
        where T : UiharuWindowBase
    {
        return WindowRegistry.FirstOrNull(typeof(T)) as T;
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

    /// <summary>
    /// 当前焦点窗口
    /// </summary>
    /// <returns></returns>
    public static Window GetFocusWindow()
    {
        if (_windowStack.Count > 0) return _windowStack.Peek();
        Window? selectedWindow = null;
        foreach (var win in WindowRegistry.All())
        {
            if (win.IsFocused) return win;
            if (win.IsActive && win.IsVisible && win.WindowState != WindowState.Minimized) selectedWindow = win;
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
        WindowRegistry.FirstOrNull(type)?.Close();
    }

    /// <summary>
    /// 把窗口收进缓存（关闭时隐藏、待下次复用）。见 <see cref="WindowCache.TryCache"/>。
    /// </summary>
    public static bool TryCacheWindow(UiharuWindowBase win) => WindowCache.TryCache(win);

    /// <summary>
    /// 窗口重新显示时调用，把它从缓存集合里移出。见 <see cref="WindowCache.MarkShown"/>。
    /// </summary>
    public static void MarkWindowShown(UiharuWindowBase win) => WindowCache.MarkShown(win);

    /// <summary>
    /// 该窗口这次 Close 是否由 LRU 强制触发（必须真关）。见 <see cref="WindowCache.IsForceClosing"/>。
    /// </summary>
    public static bool IsForceClosing(UiharuWindowBase win) => WindowCache.IsForceClosing(win);

    public static void RemoveWindow(UiharuWindowBase win)
    {
        WindowCache.Drop(win);
        WindowRegistry.Remove(win);
    }

    public static void RefreshMacApplicationActivationPolicy()
    {
        _activationPolicy.SetRegularMode(HasVisibleMacRegularModeWindow());
    }

    private static bool HasVisibleMacRegularModeWindow()
    {
        foreach (var win in WindowRegistry.All())
        {
            if (win.ContributesToMacRegularMode && win.IsVisible && win.WindowState != WindowState.Minimized)
                return true;
        }

        return false;
    }

    /// <summary>
    ///  在屏幕显示一张截图(当前鼠标位置)
    /// </summary>
    /// <param name="image"></param>
    /// <param name="startMousePos">开始截图的鼠标位置</param>
    /// <param name="endMousePos">结束截图的鼠标位置</param>
    /// <param name="size">显示尺寸（DIP），null 表示按图片自身推算</param>
    public static void ShowPreviewImageWindowAtMousePosition(Bitmap? image, PixelPoint startMousePos,
        PixelPoint endMousePos, Size? size = null, PixelPoint? anchor = null)
    {
        ShowPreviewImageWindowAtMousePosition(image, size,
            endMousePos.X > startMousePos.X ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            endMousePos.Y > startMousePos.Y ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            anchor);
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
        VerticalAlignment verticalAlignment = VerticalAlignment.Top,
        PixelPoint? anchor = null)
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
            (window) => { window.SetImage(image, size, null, horizontalAlignment, verticalAlignment, anchor); }, isMulti: true);
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

    /// <summary>
    /// 弹出通用的文本编辑窗
    /// </summary>
    /// <param name="content">初始内容</param>
    /// <param name="owner">属主窗口，为空取当前焦点窗口</param>
    /// <param name="title">窗口标题。<b>说清楚在改什么</b>——四个调用方改的东西各不相同；
    /// 为空则用通用的「编辑」</param>
    /// <returns>确定则返回编辑后的文本，取消返回 null</returns>
    public static async Task<string?> ShowStringEditWindow(string content, Window? owner = null,
        string? title = null)
    {
        StringContentEditWindow window = new StringContentEditWindow();
        if (IconUtils.DefaultAppIcon != null) window.Icon = new WindowIcon(IconUtils.DefaultAppIcon);
        if (!string.IsNullOrEmpty(title)) window.Title = title;
        window.DataContext = new StringContentEditWindowViewModel(content, null);
        return await window.ShowDialog<string?>(owner ?? UIManager.GetFocusWindow());
    }
}
