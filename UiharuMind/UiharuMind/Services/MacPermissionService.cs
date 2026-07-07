using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace UiharuMind.Services;

public static class MacPermissionService
{
    private const string ApplicationServicesFramework =
        "/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices";
    private const string CoreGraphicsFramework =
        "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";

    [DllImport(ApplicationServicesFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool AXIsProcessTrusted();

    [DllImport(CoreGraphicsFramework)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();

    public static bool IsAccessibilityGranted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        return AXIsProcessTrusted();
    }

    public static bool IsScreenRecordingGranted()
    {
        if (!OperatingSystem.IsMacOS()) return true;
        // 需要 macOS 10.15+
        return OperatingSystem.IsMacOSVersionAtLeast(10, 15) && CGPreflightScreenCaptureAccess();
    }

    public static void OpenAccessibilitySettings() => OpenPrivacyPane("Privacy_Accessibility");
    public static void OpenScreenRecordingSettings() => OpenPrivacyPane("Privacy_ScreenCapture");
    public static void OpenInputMonitoringSettings() => OpenPrivacyPane("Privacy_ListenEvent");

    private static void OpenPrivacyPane(string paneId)
    {
        if (!OperatingSystem.IsMacOS()) return;

        string url = OperatingSystem.IsMacOSVersionAtLeast(13)
            ? $"x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?{paneId}"
            : $"x-apple.systempreferences:com.apple.preference.security?{paneId}";

        try
        {
            Process.Start("open", url);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open privacy settings: {ex.Message}");
        }
    }
}