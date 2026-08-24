using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Svg.Skia;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Shared.Services;

namespace UiharuMind.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        if (TryRelaunchAsAdministrator(args)) return;

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
        GC.KeepAlive(typeof(SvgImageExtension).Assembly);
        GC.KeepAlive(typeof(Avalonia.Svg.Skia.Svg).Assembly);
    }

    private static bool TryRelaunchAsAdministrator(string[] args)
    {
        if (!OperatingSystem.IsWindows() ||
            Debugger.IsAttached ||
            !ConfigManager.Instance.Setting.EnableFullscreenGameInputSupport)
        {
            return false;
        }

        return ApplicationRestartService.TryRestartAsAdministrator(args);
    }

    /// <summary>
    /// Linux 显示后端切换开关。取值 wayland 时改用原生 Wayland 后端，其余情况走 X11(XWayland)。
    /// </summary>
    private const string LinuxBackendVariable = "UIHARU_LINUX_BACKEND";

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => ConfigureWindowingBackend(AppBuilder.Configure<App>())
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions() { ShowInDock = false });

    /// <summary>
    /// 选择窗口后端。
    ///
    /// Linux 默认钉死 X11(XWayland)：纯 Wayland 协议里不存在「把窗口放到坐标 (x,y)」这个能力，
    /// 而环形菜单、翻译窗、截图预览、录制指示器等交互全都依赖按鼠标位置定位。
    /// GNOME 49 起系统已无 X11 会话，但 XWayland 仍默认安装启用，本应用作为 X11 客户端照常工作。
    /// 设 UIHARU_LINUX_BACKEND=wayland 可切到原生后端做对照实验（届时上述定位会失效）。
    /// </summary>
    /// <param name="builder">应用构建器</param>
    /// <returns>已选定后端的构建器</returns>
    private static AppBuilder ConfigureWindowingBackend(AppBuilder builder)
    {
        return builder.UsePlatformDetect();
        if (!OperatingSystem.IsLinux()) return builder.UsePlatformDetect();

        var backend = Environment.GetEnvironmentVariable(LinuxBackendVariable);
        if (string.Equals(backend, "wayland", StringComparison.OrdinalIgnoreCase))
        {
            // 带回退：合成器不可用时自动退回 X11，避免开关设错就起不来
            return builder.UseWaylandWithFallback().UseSkia();
        }

        return builder.UseX11().UseSkia();
    }
}
