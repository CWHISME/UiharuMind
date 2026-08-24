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
using UiharuMind.Shared.Shell;
using UiharuMind.Shared.Windows;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core;
using UiharuMind.Core.Input;
using UiharuMind.Core.Input.Linux;
using UiharuMind.Features.ScreenCapture;

namespace UiharuMind.Shared.Services;

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
        get
        {
            var (x, y) = InputManager.GetPointerPosition();
            return new PixelPoint(x, y);
        }
        set => InputManager.MouseData = new MouseEventData() { X = (short)value.X, Y = (short)value.Y };
    }

    /// <summary>
    /// 全局鼠标位置当前是否可信。
    /// 纯 Wayland 下客户端无法查询全局光标位置，此时「在鼠标处弹出」的窗口应退化为居中显示。
    /// </summary>
    public bool IsMousePositionReliable => InputManager.IsPointerPositionAvailable;

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
        SyncLinuxDesktopMetrics();
    }

    /// <summary>
    /// 把虚拟桌面尺寸同步给 Core。
    /// uinput 的绝对定位设备用归一化坐标，只有知道桌面多大才能把像素换算过去，
    /// 而这个尺寸只有 UI 层拿得到。
    /// </summary>
    public void SyncLinuxDesktopMetrics()
    {
        if (!UiharuCoreManager.Instance.IsLinux) return;

        var screens = _target.Screens.All;
        if (screens.Count == 0) return;

        int right = screens.Max(screen => screen.Bounds.Right);
        int bottom = screens.Max(screen => screen.Bounds.Bottom);
        LinuxDesktopMetrics.Update(right, bottom);
    }

    /// <summary>
    /// 获取当前鼠标所在的屏幕
    /// </summary>
    /// <returns></returns>
    public Screen MouseScreen => _target.Screens.ScreenFromPoint(MousePosition) ?? App.DummyWindow.Screens.Primary ?? App.DummyWindow.Screens.All[0];

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
                window is not DummyWindow and not ApplicationNotificationWindow and not UiharuMessageBoxWindow and not ScreenCapturePreviewWindow &&
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
        return screen;
    }
}