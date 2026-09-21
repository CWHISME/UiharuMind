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

namespace UiharuMind.Core.Core.Utils;

/// <summary>
/// Linux 桌面环境，用于选择截屏等原生 CLI 工具
/// </summary>
public enum LinuxDesktopEnvironment
{
    Other,
    Gnome,
    Kde,
    Wlr
}

public static class PlatformUtils
{
    public static bool IsWindows =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
            System.Runtime.InteropServices.OSPlatform.Windows);

    public static bool IsLinux => System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime
        .InteropServices.OSPlatform
        .Linux);

    public static bool IsMacOS =>
        System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform
            .OSX);

    public static bool IsWayland =>
        string.Equals(
            System.Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"),
            "wayland",
            System.StringComparison.OrdinalIgnoreCase);

    public static LinuxDesktopEnvironment DesktopEnvironment
    {
        get
        {
            var de = System.Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? string.Empty;
            if (de.Contains("GNOME", System.StringComparison.OrdinalIgnoreCase)) return LinuxDesktopEnvironment.Gnome;
            if (de.Contains("KDE", System.StringComparison.OrdinalIgnoreCase)) return LinuxDesktopEnvironment.Kde;
            if (de.Contains("sway", System.StringComparison.OrdinalIgnoreCase) ||
                de.Contains("Hyprland", System.StringComparison.OrdinalIgnoreCase) ||
                de.Contains("wlroots", System.StringComparison.OrdinalIgnoreCase)) return LinuxDesktopEnvironment.Wlr;
            return LinuxDesktopEnvironment.Other;
        }
    }
}