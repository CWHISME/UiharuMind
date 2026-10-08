using System;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

/// <summary>
/// macOS 输入法候选框偶尔不出来的诊断：任何文本框获得焦点时记一行当时的应用与窗口状态。
/// 用来分辨两种猜测——一是在浮窗里打字时本应用并没真激活（浮窗转成了不激活应用的 panel），
/// 二是运行中切换 Dock 图标模式（accessory / regular）后文字输入状态没跟上。查明后可删
/// </summary>
public static class MacInputDiagnostics
{
    private static string? _last; //和上一条一样就不重复记

    /// <summary>
    /// 挂上（应用启动时调一次；非 macOS 什么都不做）
    /// </summary>
    public static void Install()
    {
        if (!PlatformUtils.IsMacOS) return;
        InputElement.GotFocusEvent.AddClassHandler<TextBox>((box, _) => Record(box));
    }

    private static void Record(TextBox box)
    {
        try
        {
            string line = Describe(box);
            if (line == _last) return;
            _last = line;
            Log.Debug($"IME diag: {line}");
        }
        catch (Exception e)
        {
            Log.Debug($"IME diag failed: {e.Message}");
        }
    }

    private static string Describe(TextBox box)
    {
        IntPtr pool = MacNative.AutoreleasePoolPush();
        try
        {
            IntPtr app = MacNative.SharedApplication();
            bool active = (MacNative.SendNint(app, MacNative.Selector("isActive")) & 0xFF) != 0;
            nint policy = MacNative.SendNint(app, MacNative.Selector("activationPolicy"));

            TopLevel? top = TopLevel.GetTopLevel(box);
            string windowType = top?.GetType().Name ?? "none";
            string nativeClass = "none";
            bool isKey = false;
            if (top is Window window && MacNative.TryGetNsWindow(window, out IntPtr nsWindow))
            {
                nativeClass = ClassName(nsWindow);
                isKey = (MacNative.SendNint(nsWindow, MacNative.Selector("isKeyWindow")) & 0xFF) != 0;
            }

            IntPtr keyWindow = MacNative.SendPtr(app, MacNative.Selector("keyWindow"));
            IntPtr workspace = MacNative.SendPtr(MacNative.GetClass("NSWorkspace"), MacNative.Selector("sharedWorkspace"));
            IntPtr front = MacNative.SendPtr(workspace, MacNative.Selector("frontmostApplication"));
            IntPtr context = MacNative.SendPtr(MacNative.GetClass("NSTextInputContext"),
                MacNative.Selector("currentInputContext"));
            string source = context == IntPtr.Zero
                ? "none"
                : Text(MacNative.SendPtr(context, MacNative.Selector("selectedKeyboardInputSource")));

            return $"focus in {windowType} ({nativeClass}, key={isKey}); app active={active}, " +
                   $"policy={PolicyName(policy)}, app key window={(keyWindow == IntPtr.Zero ? "none" : ClassName(keyWindow))}, " +
                   $"frontmost={Text(MacNative.SendPtr(front, MacNative.Selector("localizedName")))}, " +
                   $"input context={(context == IntPtr.Zero ? "none" : "yes")}, source={source}";
        }
        finally
        {
            MacNative.AutoreleasePoolPop(pool);
        }
    }

    private static string PolicyName(nint policy) => policy switch
    {
        0 => "regular",
        1 => "accessory",
        2 => "prohibited",
        _ => policy.ToString()
    };

    private static string ClassName(IntPtr obj) => Text(MacNative.SendPtr(obj, MacNative.Selector("className")));

    private static string Text(IntPtr nsString) => nsString == IntPtr.Zero
        ? "none"
        : Marshal.PtrToStringUTF8(MacNative.SendPtr(nsString, MacNative.Selector("UTF8String"))) ?? "none";
}
