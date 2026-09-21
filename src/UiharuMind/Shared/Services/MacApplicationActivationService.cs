using System;
using System.Runtime.InteropServices;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;
using UiharuMind.Shared.Services.Native;

namespace UiharuMind.Shared.Services;

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
            var app = MacNative.SharedApplication();
            if (app == IntPtr.Zero) return;

            TryTransformProcessType(isRegular);
            MacNative.SendBoolRetLong(app, MacNative.Selector("setActivationPolicy:"), policy);
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
            var app = MacNative.SharedApplication();
            if (app == IntPtr.Zero) return;

            MacNative.SendBool(app, MacNative.Selector("activateIgnoringOtherApps:"), true);
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
