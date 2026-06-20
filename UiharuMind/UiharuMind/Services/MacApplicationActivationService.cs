using System;
using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Services;

public static class MacApplicationActivationService
{
    private const long RegularPolicy = 0;
    private const long AccessoryPolicy = 1;
    private const uint ProcessTransformToForegroundApplication = 1;
    private const uint ProcessTransformToUIElementApplication = 4;

    private static long? _currentPolicy;

    public static void SetRegularMode(bool isRegular)
    {
        if (!PlatformUtils.IsMacOS) return;

        var policy = isRegular ? RegularPolicy : AccessoryPolicy;
        if (_currentPolicy == policy) return;

        try
        {
            var app = GetSharedApplication();
            if (app == IntPtr.Zero) return;

            TryTransformProcessType(isRegular);
            SetActivationPolicy(app, SelRegisterName("setActivationPolicy:"), policy);
            _currentPolicy = policy;
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to update macOS activation policy: {e.Message}");
        }
    }

    public static void ActivateIgnoringOtherApps()
    {
        if (!PlatformUtils.IsMacOS) return;

        try
        {
            var app = GetSharedApplication();
            if (app == IntPtr.Zero) return;

            ActivateIgnoringOtherApps(app, SelRegisterName("activateIgnoringOtherApps:"), true);
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to activate macOS application: {e.Message}");
        }
    }

    private static void TryTransformProcessType(bool isRegular)
    {
        var processSerialNumber = new ProcessSerialNumber();
        var error = GetCurrentProcess(ref processSerialNumber);
        if (error != 0)
        {
            Log.Warning($"Failed to get macOS current process: {error}");
            return;
        }

        var targetType = isRegular
            ? ProcessTransformToForegroundApplication
            : ProcessTransformToUIElementApplication;
        error = TransformProcessType(ref processSerialNumber, targetType);
        if (error != 0)
        {
            Log.Warning($"Failed to transform macOS process type: {error}");
        }
    }

    private static IntPtr GetSharedApplication()
    {
        var nsApplication = ObjcGetClass("NSApplication");
        if (nsApplication == IntPtr.Zero) return IntPtr.Zero;

        return GetSharedApplication(nsApplication, SelRegisterName("sharedApplication"));
    }

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_getClass")]
    private static extern IntPtr ObjcGetClass(string className);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "sel_registerName")]
    private static extern IntPtr SelRegisterName(string selectorName);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern IntPtr GetSharedApplication(IntPtr receiver, IntPtr selector);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool SetActivationPolicy(IntPtr receiver, IntPtr selector, long activationPolicy);

    [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")]
    private static extern void ActivateIgnoringOtherApps(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool flag);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int GetCurrentProcess(ref ProcessSerialNumber processSerialNumber);

    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern int TransformProcessType(ref ProcessSerialNumber processSerialNumber, uint transformState);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessSerialNumber
    {
        public uint HighLongOfPsn;
        public uint LowLongOfPsn;
    }
}
