using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Platform;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

public static class WindowActivationService
{
    public static void Activate(Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            window.Activate();
            return;
        }

        try
        {
            ActivateWindowsWindow(window);
        }
        catch (Exception e)
        {
            Log.Warning("Window activation failed: " + e.Message);
            window.Activate();
        }
    }

    [SupportedOSPlatform("windows")]
    private static void ActivateWindowsWindow(Window window)
    {
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();

        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "HWND")
        {
            return;
        }

        var hwnd = handle.Handle;
        if (Win32Native.IsIconic(hwnd)) Win32Native.ShowWindow(hwnd, Win32Native.SwRestore);

        var foregroundWindow = Win32Native.GetForegroundWindow();
        var foregroundThreadId = foregroundWindow == IntPtr.Zero
            ? 0
            : Win32Native.GetWindowThreadProcessId(foregroundWindow, out _);
        var currentThreadId = Win32Native.GetCurrentThreadId();
        var attached = foregroundThreadId != 0 &&
                       foregroundThreadId != currentThreadId &&
                       Win32Native.AttachThreadInput(currentThreadId, foregroundThreadId, true);

        try
        {
            // if (window.Topmost)
            // {
            //     Win32Native.SetWindowPos(hwnd, Win32Native.HwndTopmost, 0, 0, 0, 0,
            //         Win32Native.SwpNoMove | Win32Native.SwpNoSize | Win32Native.SwpNoActivate);
            // }
            // else
            // {
            //     Win32Native.SetWindowPos(hwnd, Win32Native.HwndTopmost, 0, 0, 0, 0,
            //         Win32Native.SwpNoMove | Win32Native.SwpNoSize | Win32Native.SwpNoActivate);
            //     Win32Native.SetWindowPos(hwnd, Win32Native.HwndNoTopmost, 0, 0, 0, 0,
            //         Win32Native.SwpNoMove | Win32Native.SwpNoSize | Win32Native.SwpNoActivate);
            // }
            Win32Native.BringWindowToTop(hwnd);
            Win32Native.SetForegroundWindow(hwnd);
            Win32Native.SetFocus(hwnd);
        }
        finally
        {
            if (attached) Win32Native.AttachThreadInput(currentThreadId, foregroundThreadId, false);
        }
    }

}
