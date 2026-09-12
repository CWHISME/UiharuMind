using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;

namespace UiharuMind.Shared.Services;

public static class OverlayWindowService
{
    public static void ApplyNativeNonInteractiveStyle(Window window)
    {
        if (OperatingSystem.IsWindows())
        {
            ApplyWindowsClickThroughStyle(window);
        }
        else if (OperatingSystem.IsMacOS())
        {
            ApplyMacNonInteractiveStyle(window);
        }
    }

    /// <summary>
    /// 全屏截图标注层：macOS 下把窗口抬到 screenSaver 层级并允许跨 Space，
    /// 盖住菜单栏/Dock（Topmost 只到 floating 层，盖不住菜单栏）。Windows 下 Topmost 已够，无需处理。
    /// 在窗口 Show 之后调用，Show 之前 native 行为可能被重置。
    /// </summary>
    /// <param name="window">全屏覆盖窗口</param>
    public static void ApplyNativeFullscreenOverlayStyle(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        ApplyMacFullscreenOverlayStyle(window);
    }

    [SupportedOSPlatform("windows")]
    private static void ApplyWindowsClickThroughStyle(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "HWND") return;

        var hwnd = handle.Handle;
        var exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var newExStyle = new IntPtr(exStyle.ToInt64() | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, newExStyle);
    }

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020L;
    private const long WS_EX_TOOLWINDOW = 0x00000080L;
    private const long WS_EX_NOACTIVATE = 0x08000000L;

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(hWnd, nIndex)
            : new IntPtr(GetWindowLong32(hWnd, nIndex));
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8
            ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong)
            : new IntPtr(SetWindowLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [SupportedOSPlatform("macos")]
    private static void ApplyMacNonInteractiveStyle(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;

        var nsWindow = handle.Handle;
        ObjcMsgSendBool(nsWindow, SelRegisterName("setIgnoresMouseEvents:"), true);
    }

    private const long NsScreenSaverWindowLevel = 1000;
    private const ulong NsWindowCollectionBehaviorCanJoinAllSpaces = 1;
    private const ulong NsWindowCollectionBehaviorFullScreenAuxiliary = 1u << 8;

    [SupportedOSPlatform("macos")]
    private static void ApplyMacFullscreenOverlayStyle(Window window)
    {
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;

        var nsWindow = handle.Handle;
        ObjcMsgSendLong(nsWindow, SelRegisterName("setLevel:"), NsScreenSaverWindowLevel);
        ObjcMsgSendULong(nsWindow, SelRegisterName("setCollectionBehavior:"),
            NsWindowCollectionBehaviorCanJoinAllSpaces | NsWindowCollectionBehaviorFullScreenAuxiliary);
    }

    /// <summary>
    /// 钉图窗：只把层级抬到菜单栏之上（与遮罩同值），不碰 Space 归属。
    /// 普通窗口层级在菜单栏之下，只能滑到它底下；抬层级后才能拖上去盖住它。
    /// 在窗口 Show 之后调用。
    /// </summary>
    /// <param name="window">钉图窗口</param>
    public static void ApplyNativePinAboveMenuBarStyle(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;
        var handle = window.TryGetPlatformHandle();
        if (handle == null || handle.Handle == IntPtr.Zero) return;
        ObjcMsgSendLong(handle.Handle, SelRegisterName("setLevel:"), NsScreenSaverWindowLevel);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CocoaRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendRect(
        IntPtr receiver,
        IntPtr selector,
        CocoaRect rect,
        byte display,
        byte animate);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool NativeSetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    /// <summary>
    /// 把位置与内容尺寸一次提交给原生窗口，macOS 走 setFrame:display:animate:，
    /// Windows 走 SetWindowPos（位置加尺寸单次调用）。
    /// Avalonia 托管层里 Position 立即生效、Width/Height 要走布局后到，分开设逐格撕裂闪烁。
    /// 成功后调用方只需同步 Width/Height 供内容布局，Position 不必再设。
    /// </summary>
    /// <param name="window">目标窗口</param>
    /// <param name="position">窗口左上（本平台 Position 口径：macOS 是 point，Windows 是物理像素）</param>
    /// <param name="clientSize">内容尺寸（DIP）</param>
    /// <returns>成功 true；其他平台或任何失败 false，调用方走托管老路</returns>
    public static bool TrySetWindowFrame(this Window window, PixelPoint position, Size clientSize)
    {
        if (OperatingSystem.IsMacOS()) return TrySetWindowFrameMac(window, position, clientSize);
        if (OperatingSystem.IsWindows()) return TrySetWindowFrameWin(window, position, clientSize);
        return false;
    }

    private static bool TrySetWindowFrameWin(Window window, PixelPoint position, Size clientSize)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "HWND") return false;
            var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary;
            double scaling = screen?.Scaling ?? 1.0;
            int width = (int)Math.Round(clientSize.Width * scaling);
            int height = (int)Math.Round(clientSize.Height * scaling);
            if (width <= 0 || height <= 0) return false;
            // 与 Avalonia Win32 后端同 flags，只是多带上尺寸一次提交
            NativeSetWindowPos(handle.Handle, IntPtr.Zero, position.X, position.Y, width, height,
                SwpNoZOrder | SwpNoActivate);
            return true;
        }
        catch
        {
            // native 调用失败就回退托管老路，不能把缩放搞坏
            return false;
        }
    }

    private static bool TrySetWindowFrameMac(Window window, PixelPoint position, Size clientSize)
    {
        try
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero) return false;
            var primary = window.Screens.Primary;
            if (primary == null) return false;
            if (clientSize.Width <= 0 || clientSize.Height <= 0) return false;

            // chrome 取 frame 与 content 的当前差值，本窗扩展 client area，正常接近 0
            var frameSize = window.FrameSize;
            var currentClient = window.ClientSize;
            double chromeWidth = frameSize.HasValue ? Math.Max(0, frameSize.Value.Width - currentClient.Width) : 0;
            double chromeHeight = frameSize.HasValue ? Math.Max(0, frameSize.Value.Height - currentClient.Height) : 0;
            double frameHeight = clientSize.Height + chromeHeight;

            // 与 Avalonia native 的 ConvertPointY 同口径：以 primary 顶为基准翻转 Y
            double primaryTop = primary.Bounds.Y + primary.Bounds.Height;
            var rect = new CocoaRect
            {
                X = position.X,
                Y = primaryTop - position.Y - frameHeight,
                Width = clientSize.Width + chromeWidth,
                Height = frameHeight
            };
            ObjcMsgSendRect(handle.Handle, SelRegisterName("setFrame:display:animate:"), rect, 1, 0);
            return true;
        }
        catch
        {
            // native 调用失败就回退托管老路，不能把缩放搞坏
            return false;
        }
    }

    private static IntPtr _stashedMainMenu = IntPtr.Zero;

    /// <summary>
    /// 截图期间暂时收起本应用菜单：菜单标题会拦截点击（点中标题进菜单跟踪，到不了遮罩），
    /// 空菜单栏区域则会落到遮罩上。收起后除系统 Apple 菜单外整条菜单栏都可框选。
    /// 与 Suppress 配对调用（关遮罩时 Restore），重复 Suppress 以第一次为准。
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void SuppressAppMenuForCapture()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_stashedMainMenu != IntPtr.Zero) return;
        try
        {
            var app = ObjcMsgSendPtr(ObjcGetClass("NSApplication"), SelRegisterName("sharedApplication"));
            if (app == IntPtr.Zero) return;
            var menu = ObjcMsgSendPtr(app, SelRegisterName("mainMenu"));
            if (menu == IntPtr.Zero) return;
            ObjcMsgSendVoid(menu, SelRegisterName("retain"));
            _stashedMainMenu = menu;

            var empty = ObjcMsgSendPtr(ObjcMsgSendPtr(ObjcGetClass("NSMenu"), SelRegisterName("alloc")),
                SelRegisterName("init"));
            ObjcMsgSendPtr(app, SelRegisterName("setMainMenu:"), empty);
            ObjcMsgSendVoid(empty, SelRegisterName("release"));
        }
        catch
        {
            _stashedMainMenu = IntPtr.Zero;
        }
    }

    /// <summary>
    /// 恢复被 SuppressAppMenuForCapture 收起的应用菜单。没有收起过则无操作。
    /// </summary>
    [SupportedOSPlatform("macos")]
    public static void RestoreAppMenuAfterCapture()
    {
        if (!OperatingSystem.IsMacOS()) return;
        if (_stashedMainMenu == IntPtr.Zero) return;
        try
        {
            var app = ObjcMsgSendPtr(ObjcGetClass("NSApplication"), SelRegisterName("sharedApplication"));
            if (app != IntPtr.Zero)
                ObjcMsgSendPtr(app, SelRegisterName("setMainMenu:"), _stashedMainMenu);
            ObjcMsgSendVoid(_stashedMainMenu, SelRegisterName("release"));
        }
        catch
        {
            // 恢复失败不抛：菜单已空顶多影响本次运行，不把截图流程搞坏
        }
        finally
        {
            _stashedMainMenu = IntPtr.Zero;
        }
    }
    
    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string className);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr ObjcMsgSendPtr(IntPtr receiver, IntPtr selector, IntPtr arg);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendVoid(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendBool(
        IntPtr receiver,
        IntPtr selector,
        [MarshalAs(UnmanagedType.I1)] bool value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendLong(
        IntPtr receiver,
        IntPtr selector,
        long value);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ObjcMsgSendULong(
        IntPtr receiver,
        IntPtr selector,
        ulong value);
}
