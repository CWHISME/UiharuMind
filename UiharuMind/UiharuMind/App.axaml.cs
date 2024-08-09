using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using UiharuMind.Core;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Services;
using UiharuMind.ViewModels;
using UiharuMind.Views;

namespace UiharuMind;

public partial class App : Application, ILogger
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Line below is needed to remove Avalonia data validation.
        // Without this line you will get duplicate validations from both Avalonia and CT
        BindingPlugins.DataValidators.RemoveAt(0);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // desktop.MainWindow = new MainWindow
            // {
            //     DataContext = new MainViewModel()
            // };
            DummyWindow = new DummyWindow();
            desktop.MainWindow = DummyWindow;
            Clipboard = new ClipboardService(desktop.MainWindow); //desktop.MainWindow.Clipboard;
            FilesService = new FilesService(desktop.MainWindow);
            ScreensService = new ScreensService(desktop.MainWindow);
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleViewPlatform)
        {
            singleViewPlatform.MainView = new MainView
            {
                DataContext = new MainViewModel()
            };
        }

        base.OnFrameworkInitializationCompleted();

        UiharuCoreManager.Instance.Init(this);
    }

    // public new static App Current => (App)Application.Current!;
    public static DummyWindow DummyWindow { get; private set; }
    public static ClipboardService Clipboard { get; private set; }
    public static FilesService FilesService { get; private set; }
    public static ScreensService ScreensService { get; private set; }
    public static WindowNotificationManager NotificationManager { get; set; }

    public void Debug(string message)
    {
        Console.WriteLine(message);
    }

    public void Error(string message)
    {
        Console.WriteLine(message);
    }

    private void OnQuitClick(object? sender, EventArgs e)
    {
    }

    private void OnAboutClick(object? sender, EventArgs e)
    {
    }

    private void OnOpenClick(object? sender, EventArgs e)
    {
        DummyWindow.LaunchMainWindow();
    }
}