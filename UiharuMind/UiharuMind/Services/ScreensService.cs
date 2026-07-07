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

using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using SharpHook.Data;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Input;
using UiharuMind.Views;
using UiharuMind.Views.Common;
using UiharuMind.Views.Windows.Common;

namespace UiharuMind.Services;

/// <summary>
/// 前往要注意：window 缩放屏的 scaling 是个大坑，今天(2024.9.22) 折腾了一天，总算搞明白了！！！
/// 鼠标位置和控件位置的关系并不是一致的，所以任何涉及到界面本身的坐标计算，都要根据缩放比例来计算控件位置。
/// 例如：控件的宽度、大小都是基于缩放后的，而界面的位置则是基于像素坐标。
/// </summary>
public class ScreensService
{
    private readonly Window _target;

    /// <summary>
    /// 像素上鼠标位置
    /// 注：UI 控件位置是 Point，真实的屏幕像素坐标是 PixelPoint
    /// </summary>
    public PixelPoint MousePosition
    {
        get => new(InputManager.MouseData.X, InputManager.MouseData.Y);
        set => InputManager.MouseData = new MouseEventData() { X = (short)value.X, Y = (short)value.Y };
    }

    /// <summary>
    /// 上一鼠标按下的像素位置
    /// </summary>
    public PixelPoint MousePressedPosition
    {
        get => new(InputManager.MousePressedData.X, InputManager.MousePressedData.Y);
        set => InputManager.MousePressedData = new MouseEventData() { X = (short)value.X, Y = (short)value.Y };
    }

    /// <summary>
    /// 上一次鼠标释放的像素位置
    /// </summary>
    public PixelPoint MouseReleasedPosition
    {
        get => new(InputManager.MouseReleasedData.X, InputManager.MouseReleasedData.Y);
        set => InputManager.MouseReleasedData = new MouseEventData() { X = (short)value.X, Y = (short)value.Y };
    }

    /// <summary>
    /// 以控件计算的鼠标位置
    /// </summary>
    public Point MousePositionPoint => MousePosition.ToPoint(Scaling);

    public ScreensService(Window target)
    {
        _target = target;
    }

    /// <summary>
    /// 获取当前鼠标所在的屏幕
    /// </summary>
    /// <returns></returns>
    public Screen? MouseScreen => _target.Screens.ScreenFromPoint(MousePosition);

    /// <summary>
    /// 当前屏幕缩放比例
    /// </summary>
    public double Scaling => MouseScreen?.Scaling ?? 1;

    /// <summary>
    /// 当前鼠标所在屏幕
    /// </summary>
    public int MouseScreenId => MouseScreenIndex + 1;

    /// <summary>
    /// 当前鼠标所在屏幕索引
    /// </summary>
    public int MouseScreenIndex => IndexOfScreen(_target.Screens.All, MousePosition);

    public int IndexOfScreen(IReadOnlyList<Screen> list, PixelPoint point)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Bounds.Contains(point))
            {
                return i;
            }
        }

        return -1;
    }

    public Window? GetActiveWindow()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        return desktop.Windows
            .Where(window =>
                window is not DummyWindow and not ApplicationNotificationWindow and not UiharuMessageBoxWindow &&
                window.IsVisible &&
                window.WindowState != WindowState.Minimized)
            .OrderByDescending(window => window.IsActive || window.IsFocused)
            .FirstOrDefault();
    }

    public Screen GetSafeActivationScreen()
    {
        Window? activeWindow = GetActiveWindow();
        // if (activeWindow != null) Log.Warning("找到激活的窗口:" + activeWindow.GetType().FullName);
        Screen? screen = activeWindow?.Screens.ScreenFromWindow(activeWindow);
        // if (screen != null) Log.Warning("找到激活的窗口所在屏幕:" + screen.DisplayName);
        screen ??= MouseScreen;
        screen ??= App.DummyWindow.Screens.Primary;
        return screen ?? App.DummyWindow.Screens.All[0];
    }
}