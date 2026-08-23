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
using System.IO;
using System.Threading.Tasks;
using UiharuMind.Core.Core.Process;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.Core.UiharuScreenCapture;

/// <summary>
/// Linux 截屏后端：按桌面环境探测选择原生 CLI 做交互式选区，产出 PNG 流。
/// Wayland 下应用无法自绘选区遮罩，交互交给系统工具完成。
/// </summary>
public class ScreenCaptureLinux : IScreenCapture
{
    public async Task<Stream?> CaptureRegionAsync()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"UiharuCapture_{Guid.NewGuid():N}.png");
        var (tool, args) = ResolveCommand(tmp);
        if (tool == null)
        {
            Log.Error("Linux 未找到可用的截屏工具（需 gnome-screenshot / spectacle / grim / scrot 之一）。");
            return null;
        }

        var ok = await ProcessHelper.StartProcess(tool, args);
        if (!ok || !File.Exists(tmp))
        {
            Log.Error($"通过 {tool} 截屏失败。");
            return null;
        }

        try
        {
            var bytes = await File.ReadAllBytesAsync(tmp);
            return new MemoryStream(bytes);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    private static (string? tool, string args) ResolveCommand(string tmp)
    {
        switch (PlatformUtils.DesktopEnvironment)
        {
            case LinuxDesktopEnvironment.Gnome:
                return ("gnome-screenshot", $"-a -f {tmp}");
            case LinuxDesktopEnvironment.Kde:
                return ("spectacle", $"-a -o {tmp}");
            case LinuxDesktopEnvironment.Wlr:
                if (IsCommandAvailable("slurp"))
                    return ("bash", $"-c \"grim -g $(slurp) {tmp}\"");
                return ("grim", tmp);
            default:
                if (PlatformUtils.IsWayland)
                    return ("grim", tmp);
                if (IsCommandAvailable("scrot"))
                    return ("scrot", $"-s {tmp}");
                return ("gnome-screenshot", $"-a -f {tmp}");
        }
    }

    private static bool IsCommandAvailable(string command)
    {
        return ProcessHelper.StartProcess("which", command).GetAwaiter().GetResult();
    }
}
