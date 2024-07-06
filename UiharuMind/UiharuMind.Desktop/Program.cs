using System;
using Avalonia;
using UiharuMind.Core.Core.SimpleLog;

namespace UiharuMind.Desktop;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            // 捕获未处理的异常
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Log.Error("应用程序崩溃: " + ex.Message);
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception;
        Log.Error("未处理的异常导致应用程序崩溃: " + exception);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}