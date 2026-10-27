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
#if WINDOWS
        UiharuMind.Platform.Windows.WindowsPlatform.Install();
#endif

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
        => ConfigureRendering(ConfigureWindowingBackend(AppBuilder.Configure<App>()))
            .LogToTrace()
            .With(new MacOSPlatformOptions() { ShowInDock = false });

    /// <summary>
    /// Windows：把渲染循环挪到 UI 线程。
    /// <para>
    /// 默认渲染跑在独立线程上，窗口矩形由 DWM 立刻应用、这一帧的内容却要等渲染线程，
    /// 于是贴图缩放时会出现「位置已跳过去、图还是旧尺寸」的错帧（实测落后 1~2 帧）。
    /// 挪到 UI 线程后，提交几何时的同步呈现（贴图缩放那处的 <c>FlushFrame</c>）走
    /// <c>ImmediateRenderRequested</c> 的「UI 线程内直接渲染」分支，重绘跟着落盘走，
    /// 等价于 macOS 那边 <c>setFrame:display:animate:</c> 的 <c>display:YES</c>。
    /// </para>
    /// <para>
    /// 代价：全应用渲染都走 UI 线程，UI 线程忙时掉帧更明显。若整体手感变差，把这里去掉即可回退——
    /// 同步呈现会退化成「等渲染线程画完」（阻塞式，仍正确，只是多一次线程往返），不会报错。
    /// </para>
    /// </summary>
    /// <param name="builder">应用构建器</param>
    /// <returns>配置好的构建器</returns>
    private static AppBuilder ConfigureRendering(AppBuilder builder)
    {
        if (!OperatingSystem.IsWindows()) return builder;
        return builder.With(new Win32PlatformOptions { ShouldRenderOnUIThread = true });
    }

    /// <summary>
    /// 选择窗口后端。当前全平台统一走系统默认检测。
    /// </summary>
    /// <param name="builder">应用构建器</param>
    /// <returns>已选定后端的构建器</returns>
    private static AppBuilder ConfigureWindowingBackend(AppBuilder builder) => builder.UsePlatformDetect();
}
