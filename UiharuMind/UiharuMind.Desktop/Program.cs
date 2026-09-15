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

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => ConfigureWindowingBackend(AppBuilder.Configure<App>())
            .WithInterFont()
            .LogToTrace()
            .With(new MacOSPlatformOptions() { ShowInDock = false });

    /// <summary>
    /// 选择窗口后端。当前全平台统一走系统默认检测。
    /// </summary>
    /// <param name="builder">应用构建器</param>
    /// <returns>已选定后端的构建器</returns>
    private static AppBuilder ConfigureWindowingBackend(AppBuilder builder) => builder.UsePlatformDetect();
}
